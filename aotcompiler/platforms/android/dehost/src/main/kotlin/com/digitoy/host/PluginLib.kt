package com.digitoy.host

// libgame.so plugin JNI yuzeyi (host_android.c). register: Java listesi -> native kayit (available=1); post: her thread -> inbox.
// :dehost modulunde (plugin library'leri de gorur). libgame.so app'te yuklenir (GameLib init'i); sembol adi paket+sinif ile sabit.
object PluginLib {
    @JvmStatic external fun register(name: String)
    @JvmStatic external fun post(name: String, msg: String)
}
