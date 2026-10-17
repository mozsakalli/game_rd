package com.digitoy.host

import android.app.Application
import android.content.Intent
import android.os.Bundle
import android.widget.FrameLayout
import androidx.fragment.app.FragmentActivity

// Platform SDK plugin tabani (docs/plugins.md). Plugin yazari: Assets/Plugins/<Ad>/Android/ Gradle library
// modulunde bu siniftan turer (implementation(project(":dehost"))), ayni <Ad> ile kaydolur (uretilen Plugins.kt listeler).
// Protokol iki yonde string:
//   onMessage(msg)  <- managed Plugin.Send   (MAIN thread'de cagrilir; PluginHost try/catch — exception oyunu dusurmez)
//   send(msg)       -> managed Plugin.OnMessage (her thread'den; native inbox, frame basinda cekilir)
// Lifecycle PluginHost (app) tarafindan tum plugin'lere sirayla iletilir (her biri ayri try/catch).
// Bu dosya her build'de generated/dehost altina yeniden yazilir — degistirmeyin.
abstract class DEPlugin(val name: String) {
    // Activity yasarken dolu; onApplicationCreate'te henuz yok.
    val activity: FragmentActivity get() = hostActivity ?: throw IllegalStateException("$name: activity yok (henuz onCreate olmadi)")
    val activityOrNull: FragmentActivity? get() = hostActivity
    val application: Application get() = hostApplication ?: throw IllegalStateException("$name: application yok")
    // GL yuzeyinin USTUNDE bos FrameLayout: banner / webview / native UI buraya eklenir (match_parent, tiklamayi gecirir).
    val overlay: FrameLayout get() = hostOverlay ?: throw IllegalStateException("$name: overlay yok")

    abstract fun onMessage(msg: String)

    fun send(msg: String) = PluginLib.post(name, msg)

    open fun onApplicationCreate(app: Application) {}
    open fun onCreate(saved: Bundle?) {}
    open fun onStart() {}
    open fun onResume() {}
    open fun onPause() {}
    open fun onStop() {}
    open fun onDestroy() {}
    open fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {}
    open fun onNewIntent(intent: Intent) {}
    open fun onRequestPermissionsResult(requestCode: Int, permissions: Array<String>, grantResults: IntArray) {}
    /** true = geri tusunu tuketti (oyuna gitmez). */
    open fun onBackPressed(): Boolean = false

    companion object {
        const val TAG = "digitoy"
        // Core'un yakaladigi plugin hatalari buraya (Crashlytics plugin'i recordException'a baglar); yoksa yalniz logcat.
        @JvmStatic var errorReporter: ((Throwable) -> Unit)? = null
        // PluginHost (app) doldurur.
        @JvmStatic @Volatile var hostActivity: FragmentActivity? = null
        @JvmStatic @Volatile var hostOverlay: FrameLayout? = null
        @JvmStatic @Volatile var hostApplication: Application? = null
    }
}
