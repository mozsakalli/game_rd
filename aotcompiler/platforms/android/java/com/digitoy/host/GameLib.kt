package com.digitoy.host

// libgame.so JNI yuzeyi (c_runtime/host_android.c). Yalniz GL thread'inden cagrilir (event haric).
object GameLib {
    init { System.loadLibrary("game") }

    const val EV_POINTER_DOWN = 1
    const val EV_POINTER_MOVE = 2
    const val EV_POINTER_UP = 3
    const val EV_POINTER_CANCEL = 4
    const val EV_KEY_DOWN = 10
    const val EV_KEY_UP = 11
    const val EV_TEXT = 12
    const val EV_BACK = 20
    const val EV_FOCUS = 21
    const val EV_RESIZE = 22

    @JvmStatic external fun init(dataRoot: String, writableRoot: String, fbw: Int, fbh: Int, scale: Float)
    @JvmStatic external fun frame(dt: Float, fbw: Int, fbh: Int, scale: Float): Int
    @JvmStatic external fun event(type: Int, id: Int, x: Float, y: Float, a: Int, b: Int)
    @JvmStatic external fun pause()
    @JvmStatic external fun resume()
    @JvmStatic external fun lowMemory()
    @JvmStatic external fun shutdown()
}
