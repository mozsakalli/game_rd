package com.digitoy.host

import android.app.Activity
import android.content.Context
import android.opengl.GLSurfaceView
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.view.MotionEvent
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.opengles.GL10

// DigitoyEngine Android host'u (docs/platform-hosts.md): pencere = GLSurfaceView (EGL/GLES3), dongu = Renderer,
// girdi = onTouchEvent -> GameLib.event (kuyruk), yasam dongusu = onPause/onResume -> GL thread'inde pause/resume.
// Oyun mantigi libgame.so icinde (generated.c + runtime); burasi yalnizca host.
// Bu dosya her build'de generated/java altina yeniden yazilir — degistirmeyin; SDK entegrasyonu icin
// kendi Activity'nizi/kancalarinizi app/ tarafina ekleyin.
class GameActivity : Activity() {
    private lateinit var view: GameView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        hideSystemBars()
        view = GameView(this)
        setContentView(view)
    }

    override fun onResume() {
        super.onResume()
        view.onResume()
        view.queueEvent { if (view.ready) GameLib.resume() }
    }

    override fun onPause() {
        view.queueEvent { if (view.ready) GameLib.pause() }
        view.onPause()
        super.onPause()
    }

    override fun onTrimMemory(level: Int) {
        super.onTrimMemory(level)
        view.queueEvent { if (view.ready) GameLib.lowMemory() }
    }

    // Runtime surec-omurlu ve tek init'lik (de_app_init ikinci kez cagrilamaz). Activity yok olunca sureci de
    // bitiriyoruz: bir sonraki acilis temiz surec = temiz runtime. (Yasayan Activity'de home/geri donus pause/resume'dur.)
    // GL thread'i hala kosuyor olabilir -> shutdown cagirmadan dogrudan surec sonu (durum zaten diske yazilmaz).
    override fun onDestroy() {
        super.onDestroy()
        android.os.Process.killProcess(android.os.Process.myPid())
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) hideSystemBars()
        GameLib.event(GameLib.EV_FOCUS, 0, 0f, 0f, if (hasFocus) 1 else 0, 0)
    }

    // Geri tusu oyuna olay olarak gider (GameHost.BackRequested); oyun islemezse Frame 0 doner -> finish().
    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        GameLib.event(GameLib.EV_BACK, 0, 0f, 0f, 0, 0)
    }

    fun quitFromGame() { runOnUiThread { if (!isFinishing) finish() } }

    @Suppress("DEPRECATION")
    private fun hideSystemBars() {
        if (Build.VERSION.SDK_INT >= 30) {
            window.setDecorFitsSystemWindows(false)
            window.insetsController?.let {
                it.hide(WindowInsets.Type.systemBars())
                it.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        } else {
            window.decorView.systemUiVisibility = (View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                or View.SYSTEM_UI_FLAG_LAYOUT_STABLE or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                or View.SYSTEM_UI_FLAG_FULLSCREEN)
        }
    }
}

class GameView(context: Context) : GLSurfaceView(context) {
    // Mantiksal px = fiziksel px / density (masaustu "content scale" karsiligi).
    private val density = resources.displayMetrics.density
    @Volatile var ready = false
        private set

    init {
        setEGLContextClientVersion(3)
        setEGLConfigChooser(8, 8, 8, 8, 24, 8)
        preserveEGLContextOnPause = true
        setRenderer(Renderer())
        renderMode = RENDERMODE_CONTINUOUSLY
    }

    override fun onTouchEvent(e: MotionEvent): Boolean {
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                val i = e.actionIndex
                GameLib.event(GameLib.EV_POINTER_DOWN, e.getPointerId(i), e.getX(i) / density, e.getY(i) / density, 0, 0)
            }
            MotionEvent.ACTION_MOVE ->
                for (i in 0 until e.pointerCount)
                    GameLib.event(GameLib.EV_POINTER_MOVE, e.getPointerId(i), e.getX(i) / density, e.getY(i) / density, 0, 0)
            MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> {
                val i = e.actionIndex
                GameLib.event(GameLib.EV_POINTER_UP, e.getPointerId(i), e.getX(i) / density, e.getY(i) / density, 0, 0)
            }
            MotionEvent.ACTION_CANCEL ->
                for (i in 0 until e.pointerCount)
                    GameLib.event(GameLib.EV_POINTER_CANCEL, e.getPointerId(i), e.getX(i) / density, e.getY(i) / density, 0, 0)
        }
        return true
    }

    private inner class Renderer : GLSurfaceView.Renderer {
        private var w = 0
        private var h = 0
        private var last = 0L
        private var surfaces = 0

        override fun onSurfaceCreated(gl: GL10?, config: EGLConfig?) {
            surfaces++
            if (surfaces > 1) Log.w(TAG, "GL context yeniden olusturuldu (GPU kaynaklari kayip olabilir)")
        }

        override fun onSurfaceChanged(gl: GL10?, width: Int, height: Int) {
            w = width; h = height
            if (!ready) {
                // game.pak APK assets/Build/ icinde; native taraf AssetManager fd+offset ile dogrudan okur (kopya yok).
                GameLib.init(context.assets, context.filesDir.path, w, h, density)
                ready = true
            } else {
                GameLib.event(GameLib.EV_RESIZE, 0, 0f, 0f, w, h)
            }
        }

        override fun onDrawFrame(gl: GL10?) {
            if (quitting) return
            val now = System.nanoTime()
            val dt = if (last == 0L) 0f else (now - last) / 1e9f
            last = now
            if (GameLib.frame(dt, w, h, density) == 0 && !quitting) {
                quitting = true
                (context as? GameActivity)?.quitFromGame()
            }
        }
        private var quitting = false
    }

    companion object { const val TAG = "digitoy" }
}
