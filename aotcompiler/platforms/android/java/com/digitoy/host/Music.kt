package com.digitoy.host

import android.content.res.AssetManager
import android.media.AudioAttributes
import android.media.MediaPlayer
import java.io.FileInputStream

// de_music_* koprusu (engine/native/audio_shim.c __ANDROID__ dali): MediaPlayer uzerinde handle tablosu.
// Cagrilar GL thread'inden (JNI), MediaPlayer olaylari ana thread'den gelir -> durum @Volatile.
// Durum kodlari sozlesmesi: 0 aciliyor, 1 hazir/duraklatilmis, 2 caliyor, 3 bitti, -1 hata.
// Kaynak: "asset:/..." (APK assets, pak araligi = AssetFileDescriptor + offset/len), duz dosya yolu
// (FileInputStream fd + offset/len) ya da http(s) URL. Bu dosya her build'de yeniden yazilir.
object Music {
    @JvmStatic @Volatile var assets: AssetManager? = null

    private class Item(val mp: MediaPlayer) {
        @Volatile var state = 0
        @Volatile var wantPlay = false
        @Volatile var loop = false
        var fd: java.io.Closeable? = null
    }

    private val items = arrayOfNulls<Item>(8)

    @JvmStatic fun open(pathOrUrl: String, offset: Long, length: Long): Int {
        val slot = items.indexOfFirst { it == null }
        if (slot < 0) return -1
        val mp = MediaPlayer()
        // Lambda'larin ortuk `it` parametresiyle golgelenmesin diye `item`.
        val item = Item(mp)
        try {
            mp.setAudioAttributes(AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_GAME)
                .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
            when {
                pathOrUrl.startsWith("http://") || pathOrUrl.startsWith("https://") -> mp.setDataSource(pathOrUrl)
                pathOrUrl.startsWith("asset:") -> {
                    val am = assets ?: return fail(mp)
                    val afd = am.openFd(pathOrUrl.removePrefix("asset:").trimStart('/'))
                    val len = if (length > 0) length else afd.length - offset
                    mp.setDataSource(afd.fileDescriptor, afd.startOffset + offset, len)
                    item.fd = afd
                }
                else -> {
                    val fis = FileInputStream(pathOrUrl)
                    val len = if (length > 0) length else fis.channel.size() - offset
                    mp.setDataSource(fis.fd, offset, len)
                    item.fd = fis
                }
            }
            mp.setOnPreparedListener { p ->
                if (item.state == 0) {
                    item.state = 1
                    p.isLooping = item.loop
                    if (item.wantPlay) { p.start(); item.state = 2 }
                }
            }
            mp.setOnCompletionListener { _ -> if (!item.loop) { item.state = 3; item.wantPlay = false } }
            mp.setOnErrorListener { _, _, _ -> item.state = -1; item.wantPlay = false; true }
            mp.prepareAsync()
        } catch (e: Exception) {
            return fail(mp)
        }
        items[slot] = item
        return slot
    }

    private fun fail(mp: MediaPlayer): Int { try { mp.release() } catch (_: Exception) {}; return -1 }

    private fun get(h: Int): Item? = if (h in items.indices) items[h] else null

    @JvmStatic fun close(h: Int) {
        val it = get(h) ?: return
        items[h] = null
        try { it.mp.stop() } catch (_: Exception) {}
        try { it.mp.release() } catch (_: Exception) {}
        try { it.fd?.close() } catch (_: Exception) {}
    }

    @JvmStatic fun play(h: Int) {
        val it = get(h) ?: return
        it.wantPlay = true
        when (it.state) {
            0 -> {} // onPrepared baslatir
            3 -> { try { it.mp.seekTo(0); it.mp.start(); it.state = 2 } catch (_: Exception) {} }
            1 -> { try { it.mp.start(); it.state = 2 } catch (_: Exception) {} }
        }
    }

    @JvmStatic fun pause(h: Int) {
        val it = get(h) ?: return
        it.wantPlay = false
        if (it.state == 2) { try { it.mp.pause() } catch (_: Exception) {}; it.state = 1 }
    }

    @JvmStatic fun setVolume(h: Int, v: Float) {
        val it = get(h) ?: return
        val c = v.coerceIn(0f, 1f)
        try { it.mp.setVolume(c, c) } catch (_: Exception) {}
    }

    @JvmStatic fun setLoop(h: Int, loop: Boolean) {
        val it = get(h) ?: return
        it.loop = loop
        if (it.state != 0) try { it.mp.isLooping = loop } catch (_: Exception) {}
    }

    @JvmStatic fun state(h: Int): Int = get(h)?.state ?: -1

    @JvmStatic fun position(h: Int): Double {
        val it = get(h) ?: return 0.0
        if (it.state == 0 || it.state == -1) return 0.0
        return try { it.mp.currentPosition / 1000.0 } catch (_: Exception) { 0.0 }
    }

    // Canli akis (sure bilinmiyor) -> -1.
    @JvmStatic fun duration(h: Int): Double {
        val it = get(h) ?: return 0.0
        if (it.state == 0 || it.state == -1) return 0.0
        val d = try { it.mp.duration } catch (_: Exception) { 0 }
        return if (d <= 0) -1.0 else d / 1000.0
    }

    @JvmStatic fun seek(h: Int, sec: Double) {
        val it = get(h) ?: return
        if (it.state == 0 || it.state == -1) return
        try { it.mp.seekTo((sec * 1000).toInt()) } catch (_: Exception) {}
    }

    @JvmStatic fun shutdown() { for (i in items.indices) close(i) }
}
