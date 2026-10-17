# DigitoyEngine Android kabugu — R8 keep kurallari (ilk uretimde yazilir, sonra size aittir).
# JNI'den ISIMLE cagrilan host siniflari: libgame.so (host_android.c) FindClass/GetStaticMethodID ile bulur.
# SDK'lar kendi consumer-rules.pro'sunu tasir; buraya SDK kurali eklemek gerekmez (docs/plugins.md).
-keep class com.digitoy.host.GameLib { *; }
-keep class com.digitoy.host.PluginLib { *; }
-keep class com.digitoy.host.PluginHost { public static void dispatch(java.lang.String, java.lang.String); }
-keep class com.digitoy.host.Music { *; }
-keep class com.digitoy.host.Prefs { *; }
-keep class com.digitoy.host.ManagedException { *; }
-keep class com.digitoy.host.GameApplication
-keep class com.digitoy.host.GameActivity
# native metot adlari (Java_com_digitoy_host_* sembolleri .so'da sabit)
-keepclasseswithmembernames class * { native <methods>; }
# plugin siniflari uretilen Plugins.kt'den dogrudan new ile kurulur -> R8 zaten tutar; reflection yok.
