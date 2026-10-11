package com.digitoy.host

import android.content.Context
import android.content.SharedPreferences

// PlayerPrefs koprusu (engine/native/de_fs.c __ANDROID__ prefs bolumu, JNI_OnLoad'da baglanir):
// SharedPreferences "digitoy_prefs". Degerler string (tip donusumu managed'da). Her yazim apply()
// (asenkron, surec olse de kaybolmaz); save() commit ile senkron flush. Bu dosya her build'de yeniden yazilir.
object Prefs {
    @Volatile private var sp: SharedPreferences? = null

    @JvmStatic fun init(context: Context) {
        if (sp == null) sp = context.applicationContext.getSharedPreferences("digitoy_prefs", Context.MODE_PRIVATE)
    }

    @JvmStatic fun get(key: String): String? = sp?.getString(key, null)
    @JvmStatic fun set(key: String, value: String) { sp?.edit()?.putString(key, value)?.apply() }
    @JvmStatic fun delete(key: String) { sp?.edit()?.remove(key)?.apply() }
    @JvmStatic fun deleteAll() { sp?.edit()?.clear()?.apply() }
    @JvmStatic fun has(key: String): Boolean = sp?.contains(key) ?: false
    @JvmStatic fun save() { sp?.edit()?.commit() }
}
