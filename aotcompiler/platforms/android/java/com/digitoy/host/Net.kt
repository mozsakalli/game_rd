package com.digitoy.host

import java.io.InputStream
import java.io.OutputStream
import java.net.HttpURLConnection
import java.net.InetSocketAddress
import java.net.Socket
import java.net.URI
import java.net.URL
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import javax.net.ssl.SSLSocket
import javax.net.ssl.SSLSocketFactory

// de_net koprusu (engine/native/de_net.c __ANDROID__ dali; host JNI_OnLoad de_android_net_bind baglar).
// 3. parti YOK: HTTP = HttpURLConnection, WebSocket = RFC 6455 istemcisi Socket/SSLSocket uzerinde (TLS OS'un).
// Cagrilar GL thread'inden (JNI); tamamlanmalar `done` ile native de_async kuyruguna (her thread'den guvenli).
// ZERO-GC: mesaj basina Java nesnesi yok — baglanti basina buyuyen alim tamponu, bitisik gonderim ring'i,
// `done` native'e senkron kopyalar (tampon hemen yeniden kullanilir). Bu dosya her build'de yeniden yazilir.
object Net {
    const val EVENT = 0x40000000
    const val WS_OPEN = EVENT or 1
    const val WS_MESSAGE = EVENT or 2
    const val WS_CLOSE = 2
    private const val WS_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

    // job, code, data[off, off+len), a, b -> de_async_complete (native GetByteArrayRegion ile tek kopya)
    @JvmStatic external fun done(job: Int, code: Int, data: ByteArray?, off: Int, len: Int, a: Int, b: Int)

    private val pool = Executors.newCachedThreadPool { r -> Thread(r, "de-net").apply { isDaemon = true } }
    private val https = ConcurrentHashMap<Int, HttpURLConnection>()
    private val httpCancelled = ConcurrentHashMap.newKeySet<Int>()
    private val sockets = ConcurrentHashMap<Int, Ws>()

    private fun fail(job: Int, msg: String?) {
        val b = (msg ?: "network error").toByteArray(Charsets.UTF_8)
        done(job, -1, b, 0, b.size, 0, 0)
    }

    // "K: V\n" satirlari
    private inline fun eachHeader(headers: String?, f: (String, String) -> Unit) {
        if (headers.isNullOrEmpty()) return
        for (line in headers.split('\n')) {
            val i = line.indexOf(':')
            if (i > 0) f(line.substring(0, i).trim(), line.substring(i + 1).trim())
        }
    }

    // ---------------------------------------------------------------- HTTP
    @JvmStatic fun request(job: Int, method: String, url: String, headers: String?, body: ByteArray?, timeoutMs: Int) {
        pool.execute { doRequest(job, method, url, headers, body, if (timeoutMs > 0) timeoutMs else 30000) }
    }

    @JvmStatic fun cancel(job: Int) {
        httpCancelled.add(job)
        try { https.remove(job)?.disconnect() } catch (_: Exception) {}
    }

    private fun doRequest(job: Int, method: String, url: String, headers: String?, body: ByteArray?, timeoutMs: Int) {
        var conn: HttpURLConnection? = null
        try {
            conn = URL(url).openConnection() as HttpURLConnection
            https[job] = conn
            conn.connectTimeout = timeoutMs
            conn.readTimeout = timeoutMs
            conn.requestMethod = method
            conn.instanceFollowRedirects = true
            eachHeader(headers) { k, v -> conn.setRequestProperty(k, v) }
            if (body != null && body.isNotEmpty()) {
                conn.doOutput = true
                conn.setFixedLengthStreamingMode(body.size)
                conn.outputStream.use { it.write(body) }
            }
            val status = conn.responseCode
            // Baslik blogu WinHTTP/fetch ile ayni bicim: durum satiri + "K: V\r\n" + bos satir
            val sb = StringBuilder(256)
            sb.append("HTTP/1.1 ").append(status).append(' ').append(conn.responseMessage ?: "").append("\r\n")
            for ((k, vs) in conn.headerFields) {
                if (k == null) continue
                for (v in vs) sb.append(k).append(": ").append(v).append("\r\n")
            }
            sb.append("\r\n")
            val hb = sb.toString().toByteArray(Charsets.UTF_8)
            val hint = conn.contentLength
            var buf = ByteArray(hb.size + (if (hint > 0) hint else 16384))
            System.arraycopy(hb, 0, buf, 0, hb.size)
            var len = hb.size
            val input: InputStream? = try { if (status >= 400) conn.errorStream else conn.inputStream } catch (_: Exception) { conn.errorStream }
            if (input != null) input.use { s ->
                while (true) {
                    if (len == buf.size) buf = buf.copyOf(buf.size * 2)
                    val r = s.read(buf, len, buf.size - len)
                    if (r <= 0) break
                    len += r
                }
            }
            https.remove(job)
            if (httpCancelled.remove(job)) return
            done(job, 1, buf, 0, len, status, hb.size)
        } catch (e: Exception) {
            https.remove(job)
            if (httpCancelled.remove(job)) return
            fail(job, e.message ?: e.javaClass.simpleName)
        } finally {
            try { conn?.disconnect() } catch (_: Exception) {}
        }
    }

    // ---------------------------------------------------------------- WebSocket
    @JvmStatic fun wsOpen(job: Int, url: String, headers: String?) {
        val ws = Ws(job, url, headers)
        sockets[job] = ws
        pool.execute { ws.run() }
    }

    // Gonderim icin Java scratch dizisi (>= len); native SetByteArrayRegion ile doldurur, sonra wsSend. Alloc yalniz buyumede.
    @JvmStatic fun wsScratch(ws: Int, len: Int): ByteArray? = sockets[ws]?.scratch(len)

    @JvmStatic fun wsSend(ws: Int, len: Int, isText: Boolean): Int = sockets[ws]?.sendScratch(len, isText) ?: -1

    @JvmStatic fun wsClose(ws: Int, code: Int, reason: String?) { sockets[ws]?.close(code, reason ?: "") }

    @JvmStatic fun wsFree(ws: Int) { sockets.remove(ws)?.free() }

    private class Ws(val job: Int, val url: String, val headers: String?) {
        @Volatile var socket: Socket? = null
        var input: InputStream? = null
        var output: OutputStream? = null
        val finished = AtomicBoolean(false) // CLOSE/-1 yazildi ya da free edildi
        @Volatile var closeSent = false
        @Volatile var closeCode = 1006
        var closeReason = ByteArray(0)
        var closeReasonLen = 0

        // alim: mesaj birikimi (parcali frame'ler) + sabit header/kontrol tamponlari
        var rbuf = ByteArray(16384)
        var rlen = 0
        val hdr = ByteArray(14)
        val ctrl = ByteArray(125)
        val mask = ByteArray(4)
        val rnd = SecureRandom()

        // gonderim: scratch (native -> Java), bitisik ring (frame'ler) + segment ring (off,len)
        var sbuf = ByteArray(4096)
        var segs = IntArray(32) // [off,len] ciftleri
        var segHead = 0
        var segCount = 0
        var writing = false
        var scratchArr = ByteArray(1024)
        val sendLock = Object()
        val writer = Runnable { writeLoop() }

        fun scratch(len: Int): ByteArray {
            if (scratchArr.size < len) scratchArr = ByteArray(maxOf(len, scratchArr.size * 2))
            return scratchArr
        }

        // ---- gonderim ring'i
        private fun ringAlloc(len: Int): Int {
            if (segCount == 0) {
                if (len > sbuf.size) sbuf = ByteArray(maxOf(len, sbuf.size * 2))
                return 0
            }
            val first = segHead * 2
            val last = ((segHead + segCount - 1) % (segs.size / 2)) * 2
            val rpos = segs[first]
            val wpos = segs[last] + segs[last + 1]
            if (wpos >= rpos) {
                if (wpos + len <= sbuf.size) return wpos
                if (len < rpos) return 0
            } else if (wpos + len < rpos) return wpos
            // sigmadi: buyut, segmentleri dogrusal yerlestir (yalniz buyumede kopya)
            var need = len
            for (i in 0 until segCount) need += segs[((segHead + i) % (segs.size / 2)) * 2 + 1]
            var cap = sbuf.size * 2
            while (cap < need) cap *= 2
            val nb = ByteArray(cap)
            var pos = 0
            for (i in 0 until segCount) {
                val s = ((segHead + i) % (segs.size / 2)) * 2
                System.arraycopy(sbuf, segs[s], nb, pos, segs[s + 1])
                segs[s] = pos
                pos += segs[s + 1]
            }
            sbuf = nb
            return pos
        }

        private fun segPush(off: Int, len: Int) {
            val cap = segs.size / 2
            if (segCount == cap) {
                val ns = IntArray(segs.size * 2)
                for (i in 0 until segCount) {
                    val s = ((segHead + i) % cap) * 2
                    ns[i * 2] = segs[s]; ns[i * 2 + 1] = segs[s + 1]
                }
                segs = ns
                segHead = 0
            }
            val s = ((segHead + segCount) % (segs.size / 2)) * 2
            segs[s] = off; segs[s + 1] = len
            segCount++
        }

        // Frame'i ring'e yazar (header + maske + maskeli payload tek gecis), yazici yoksa baslatir.
        private fun enqueueFrame(opcode: Int, src: ByteArray, srcOff: Int, len: Int): Int {
            synchronized(sendLock) {
                if (closeSent && opcode != 0x8) return -1
                if (finished.get()) return -1
                val hl = if (len < 126) 2 else if (len < 65536) 4 else 10
                val total = hl + 4 + len
                val off = ringAlloc(total)
                val b = sbuf
                var p = off
                b[p++] = (0x80 or opcode).toByte()
                when {
                    len < 126 -> b[p++] = (0x80 or len).toByte()
                    len < 65536 -> { b[p++] = (0x80 or 126).toByte(); b[p++] = (len ushr 8).toByte(); b[p++] = len.toByte() }
                    else -> {
                        b[p++] = (0x80 or 127).toByte()
                        for (i in 0 until 4) b[p++] = 0
                        b[p++] = (len ushr 24).toByte(); b[p++] = (len ushr 16).toByte(); b[p++] = (len ushr 8).toByte(); b[p++] = len.toByte()
                    }
                }
                val m0 = rnd.nextInt()
                val k0 = (m0 ushr 24).toByte(); val k1 = (m0 ushr 16).toByte(); val k2 = (m0 ushr 8).toByte(); val k3 = m0.toByte()
                b[p++] = k0; b[p++] = k1; b[p++] = k2; b[p++] = k3
                var i = 0
                while (i + 4 <= len) {
                    b[p] = (src[srcOff + i].toInt() xor k0.toInt()).toByte()
                    b[p + 1] = (src[srcOff + i + 1].toInt() xor k1.toInt()).toByte()
                    b[p + 2] = (src[srcOff + i + 2].toInt() xor k2.toInt()).toByte()
                    b[p + 3] = (src[srcOff + i + 3].toInt() xor k3.toInt()).toByte()
                    p += 4; i += 4
                }
                while (i < len) {
                    val k = when (i and 3) { 0 -> k0; 1 -> k1; 2 -> k2; else -> k3 }
                    b[p++] = (src[srcOff + i].toInt() xor k.toInt()).toByte(); i++
                }
                segPush(off, total)
                if (!writing && output != null) { writing = true; pool.execute(writer) }
            }
            return 0
        }

        private fun writeLoop() {
            while (true) {
                var off: Int; var len: Int
                synchronized(sendLock) {
                    if (segCount == 0 || finished.get()) { writing = false; return }
                    val s = segHead * 2
                    off = segs[s]; len = segs[s + 1]
                }
                try {
                    val o = output ?: throw IllegalStateException("not connected")
                    o.write(sbuf, off, len)
                    o.flush()
                } catch (e: Exception) {
                    synchronized(sendLock) { writing = false }
                    if (!closeSent) failAndClose(e.message ?: "write error")
                    return
                }
                synchronized(sendLock) {
                    segHead = (segHead + 1) % (segs.size / 2)
                    segCount--
                }
            }
        }

        fun sendScratch(len: Int, isText: Boolean): Int = enqueueFrame(if (isText) 0x1 else 0x2, scratchArr, 0, len)

        fun close(code: Int, reason: String) {
            if (closeSent || finished.get()) return
            closeSent = true
            if (output == null) { // daha baglanmadi: vazgec
                finish(-1, 0, "canceled".toByteArray(Charsets.UTF_8))
                try { socket?.close() } catch (_: Exception) {}
                return
            }
            val rb = reason.toByteArray(Charsets.UTF_8)
            val n = minOf(rb.size, 123)
            val body = ByteArray(2 + n)
            val c = if (code > 0) code else 1000
            body[0] = (c ushr 8).toByte(); body[1] = c.toByte()
            System.arraycopy(rb, 0, body, 2, n)
            enqueueFrame(0x8, body, 0, body.size)
            // karsi CLOSE gelmezse 3 s sonra soketi kapat -> okuma dongusu 1006 ile biter
            pool.execute {
                try { Thread.sleep(3000) } catch (_: InterruptedException) {}
                if (!finished.get()) try { socket?.close() } catch (_: Exception) {}
            }
        }

        fun free() {
            finished.set(true)
            try { socket?.close() } catch (_: Exception) {}
        }

        private fun finish(code: Int, a: Int, data: ByteArray?) {
            if (finished.getAndSet(true)) return
            done(job, code, data, 0, data?.size ?: 0, a, 0)
        }

        private fun failAndClose(msg: String) {
            finish(-1, 0, msg.toByteArray(Charsets.UTF_8))
            try { socket?.close() } catch (_: Exception) {}
        }

        // ---- baglanti + okuma dongusu (kendi thread'i)
        fun run() {
            try {
                connect()
            } catch (e: Exception) {
                if (!finished.get()) failAndClose(e.message ?: e.javaClass.simpleName)
                return
            }
            if (finished.get()) return
            done(job, WS_OPEN, null, 0, 0, 0, 0)
            synchronized(sendLock) { if (segCount > 0 && !writing) { writing = true; pool.execute(writer) } }
            try {
                readLoop()
            } catch (e: Exception) {
                if (!finished.get()) {
                    // biz CLOSE gonderdik ve karsi taraf TCP'yi kapattiysa el sikisma kabul (1006 yoksa saklanan kod)
                    if (closeSent) finish(WS_CLOSE, closeCode, closeReason.copyOf(closeReasonLen))
                    else failAndClose(e.message ?: e.javaClass.simpleName)
                }
            }
            try { socket?.close() } catch (_: Exception) {}
        }

        private fun connect() {
            val u = URI(url)
            val secure = u.scheme.equals("wss", true) || u.scheme.equals("https", true)
            val port = if (u.port > 0) u.port else if (secure) 443 else 80
            val host = u.host ?: throw IllegalArgumentException("url host yok")
            var path = if (u.rawPath.isNullOrEmpty()) "/" else u.rawPath
            if (!u.rawQuery.isNullOrEmpty()) path += "?" + u.rawQuery
            val s = Socket()
            s.tcpNoDelay = true
            s.keepAlive = true
            s.connect(InetSocketAddress(host, port), 15000)
            socket = s
            val sock: Socket = if (secure) {
                val ssl = (SSLSocketFactory.getDefault() as SSLSocketFactory).createSocket(s, host, port, true) as SSLSocket
                val p = ssl.sslParameters
                p.endpointIdentificationAlgorithm = "HTTPS" // sunucu adi dogrulamasi
                ssl.sslParameters = p
                ssl.startHandshake()
                socket = ssl
                ssl
            } else s
            val inp = sock.getInputStream()
            val out = sock.getOutputStream()
            val keyBytes = ByteArray(16).also { rnd.nextBytes(it) }
            val key = Base64.getEncoder().encodeToString(keyBytes)
            val req = StringBuilder(512)
            req.append("GET ").append(path).append(" HTTP/1.1\r\n")
            req.append("Host: ").append(host)
            if (port != (if (secure) 443 else 80)) req.append(':').append(port)
            req.append("\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: ").append(key).append("\r\nSec-WebSocket-Version: 13\r\n")
            eachHeader(headers) { k, v -> req.append(k).append(": ").append(v).append("\r\n") }
            req.append("\r\n")
            out.write(req.toString().toByteArray(Charsets.ISO_8859_1))
            out.flush()
            // yanit basliklari (CRLFCRLF'e dek, en cok 16 KB)
            val resp = ByteArray(16384)
            var n = 0
            while (true) {
                val c = inp.read()
                if (c < 0) throw java.io.IOException("baglanti kapandi (el sikisma)")
                if (n == resp.size) throw java.io.IOException("el sikisma yaniti cok buyuk")
                resp[n++] = c.toByte()
                if (n >= 4 && resp[n - 1] == 10.toByte() && resp[n - 2] == 13.toByte() && resp[n - 3] == 10.toByte() && resp[n - 4] == 13.toByte()) break
            }
            val lines = String(resp, 0, n, Charsets.ISO_8859_1).split("\r\n")
            val status = lines[0].split(' ')
            if (status.size < 2 || status[1] != "101") throw java.io.IOException("websocket upgrade reddedildi: " + lines[0])
            var accept: String? = null
            for (i in 1 until lines.size) {
                val l = lines[i]; val ci = l.indexOf(':')
                if (ci > 0 && l.substring(0, ci).trim().equals("Sec-WebSocket-Accept", true)) accept = l.substring(ci + 1).trim()
            }
            val expect = Base64.getEncoder().encodeToString(MessageDigest.getInstance("SHA-1").digest((key + WS_GUID).toByteArray(Charsets.ISO_8859_1)))
            if (accept != expect) throw java.io.IOException("Sec-WebSocket-Accept uyusmuyor")
            input = inp
            output = out
        }

        private fun readFully(inp: InputStream, b: ByteArray, off: Int, len: Int) {
            var got = 0
            while (got < len) {
                val r = inp.read(b, off + got, len - got)
                if (r < 0) throw java.io.IOException("baglanti kapandi")
                got += r
            }
        }

        private fun readLoop() {
            val inp = input!!
            var msgText = false
            while (!finished.get()) {
                readFully(inp, hdr, 0, 2)
                val b0 = hdr[0].toInt() and 0xFF
                val b1 = hdr[1].toInt() and 0xFF
                val fin = (b0 and 0x80) != 0
                val opcode = b0 and 0x0F
                val masked = (b1 and 0x80) != 0
                var len = (b1 and 0x7F).toLong()
                if (len == 126L) { readFully(inp, hdr, 2, 2); len = ((hdr[2].toInt() and 0xFF) shl 8 or (hdr[3].toInt() and 0xFF)).toLong() }
                else if (len == 127L) {
                    readFully(inp, hdr, 2, 8)
                    len = 0
                    for (i in 2 until 10) len = (len shl 8) or (hdr[i].toInt() and 0xFF).toLong()
                }
                if (len > 64L * 1024 * 1024) throw java.io.IOException("frame cok buyuk")
                if (masked) readFully(inp, mask, 0, 4)
                val n = len.toInt()
                if (opcode >= 0x8) {
                    // kontrol frame'i (<=125, parcalanamaz)
                    if (n > 125) throw java.io.IOException("kontrol frame'i cok buyuk")
                    readFully(inp, ctrl, 0, n)
                    if (masked) for (i in 0 until n) ctrl[i] = (ctrl[i].toInt() xor mask[i and 3].toInt()).toByte()
                    when (opcode) {
                        0x9 -> enqueueFrame(0xA, ctrl, 0, n) // ping -> pong
                        0x8 -> {
                            var code = 1005
                            var rOff = 0
                            if (n >= 2) { code = ((ctrl[0].toInt() and 0xFF) shl 8) or (ctrl[1].toInt() and 0xFF); rOff = 2 }
                            closeCode = code
                            closeReasonLen = n - rOff
                            if (closeReason.size < closeReasonLen) closeReason = ByteArray(125)
                            System.arraycopy(ctrl, rOff, closeReason, 0, closeReasonLen)
                            if (!closeSent) { closeSent = true; enqueueFrame(0x8, ctrl, 0, minOf(n, 2)) } // ayni kodla yanit
                            // yazicinin CLOSE'u gondermesine firsat ver (en cok 500 ms), sonra bitir
                            for (i in 0 until 50) {
                                val empty = synchronized(sendLock) { segCount == 0 }
                                if (empty) break
                                Thread.sleep(10)
                            }
                            finish(WS_CLOSE, code, closeReason.copyOf(closeReasonLen))
                            return
                        }
                        else -> {} // pong
                    }
                    continue
                }
                if (opcode == 0x1 || opcode == 0x2) { msgText = opcode == 0x1; rlen = 0 }
                else if (opcode != 0x0) throw java.io.IOException("bilinmeyen opcode $opcode")
                if (rlen + n > rbuf.size) {
                    var cap = rbuf.size * 2
                    while (cap < rlen + n) cap *= 2
                    rbuf = rbuf.copyOf(cap)
                }
                readFully(inp, rbuf, rlen, n)
                if (masked) for (i in 0 until n) rbuf[rlen + i] = (rbuf[rlen + i].toInt() xor mask[i and 3].toInt()).toByte()
                rlen += n
                if (fin) {
                    done(job, WS_MESSAGE, rbuf, 0, rlen, if (msgText) 1 else 0, 0) // native senkron kopyalar; rbuf yeniden kullanilir
                    rlen = 0
                }
            }
        }
    }
}
