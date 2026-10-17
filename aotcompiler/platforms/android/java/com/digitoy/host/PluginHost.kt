package com.digitoy.host

import android.app.Application
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.widget.FrameLayout
import androidx.fragment.app.FragmentActivity

// Plugin host'u: kayit, managed->plugin dispatch (main thread), lifecycle fan-out, hata duvari.
// Plugin listesi uretilen Plugins.kt'den (canli plugin'ler). Bu dosya her build'de yeniden yazilir.
object PluginHost {
    val activity: FragmentActivity? get() = DEPlugin.hostActivity
    private val main = Handler(Looper.getMainLooper())
    private val byName = HashMap<String, DEPlugin>()
    private var registered = false

    @JvmStatic fun registerAll(app: Application) {
        if (registered) return
        registered = true
        DEPlugin.hostApplication = app
        System.loadLibrary("game") // PluginLib.register JNI: Application.onCreate GameLib init'inden (GL thread) ONCE kosar; idempotent
        for (p in Plugins.all) {
            if (byName.containsKey(p.name)) { Log.e(DEPlugin.TAG, "plugin adi cakisti: ${p.name}"); continue }
            byName[p.name] = p
            PluginLib.register(p.name)
        }
        installUncaughtReporter()
    }

    // Native (de_plugin_send, render thread) -> buraya -> main thread -> plugin.onMessage. Her zaman try/catch.
    @JvmStatic fun dispatch(name: String, msg: String) {
        val p = byName[name] ?: return
        main.post { guard(p) { p.onMessage(msg) } }
    }

    private inline fun guard(p: DEPlugin, body: () -> Unit) {
        try { body() } catch (t: Throwable) { report(p, t) }
    }

    private fun report(p: DEPlugin?, t: Throwable) {
        Log.e(DEPlugin.TAG, "plugin hatasi (${p?.name ?: "?"}) — oyun devam ediyor", t)
        try { DEPlugin.errorReporter?.invoke(t) } catch (_: Throwable) {}
    }

    // Uncaught handler ZINCIRI: yalniz raporlar (plugin frame'i iceriyorsa errorReporter), sonra onceki handler'a
    // birakir (crash). Looper'i yeniden surme gibi hileler BILEREK yok (Activity yarim kalir).
    private fun installUncaughtReporter() {
        val prev = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { th, t ->
            if (byName.isNotEmpty() && isPluginFrame(t)) report(null, t)
            prev?.uncaughtException(th, t)
        }
    }

    private fun isPluginFrame(t: Throwable): Boolean {
        for (p in byName.values) {
            val cls = p.javaClass.name
            var e: Throwable? = t
            while (e != null) {
                for (f in e.stackTrace) if (f.className.startsWith(cls)) return true
                e = e.cause
            }
        }
        return false
    }

    // ---- lifecycle fan-out (GameApplication / GameActivity cagirir) ----
    fun onApplicationCreate(app: Application) { for (p in byName.values) guard(p) { p.onApplicationCreate(app) } }
    fun onCreate(a: FragmentActivity, ov: FrameLayout, saved: Bundle?) { DEPlugin.hostActivity = a; DEPlugin.hostOverlay = ov; for (p in byName.values) guard(p) { p.onCreate(saved) } }
    fun onStart() { for (p in byName.values) guard(p) { p.onStart() } }
    fun onResume() { for (p in byName.values) guard(p) { p.onResume() } }
    fun onPause() { for (p in byName.values) guard(p) { p.onPause() } }
    fun onStop() { for (p in byName.values) guard(p) { p.onStop() } }
    fun onDestroy() { for (p in byName.values) guard(p) { p.onDestroy() }; DEPlugin.hostActivity = null; DEPlugin.hostOverlay = null }
    fun onActivityResult(rq: Int, rs: Int, d: Intent?) { for (p in byName.values) guard(p) { p.onActivityResult(rq, rs, d) } }
    fun onNewIntent(i: Intent) { for (p in byName.values) guard(p) { p.onNewIntent(i) } }
    fun onRequestPermissionsResult(rq: Int, perms: Array<String>, res: IntArray) { for (p in byName.values) guard(p) { p.onRequestPermissionsResult(rq, perms, res) } }
    fun onBackPressed(): Boolean {
        for (p in byName.values) {
            var handled = false
            guard(p) { handled = p.onBackPressed() }
            if (handled) return true
        }
        return false
    }
}

// Application: plugin kaydi + onApplicationCreate (Firebase/Adjust gibi SDK'lar Application seviyesinde init ister).
// Manifest android:name=".GameApplication" (kabuk v3).
class GameApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        PluginHost.registerAll(this)
        PluginHost.onApplicationCreate(this)
    }
}
