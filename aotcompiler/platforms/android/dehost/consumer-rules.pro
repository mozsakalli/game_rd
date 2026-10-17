# :dehost consumer kurallari — app'in R8 yapilandirmasina otomatik eklenir (kabuk proguard-rules.pro kullaniciya ait,
# ilk uretimden sonra dokunulmaz; engine'in JNI'den ISIMLE buldugu yeni host siniflari buradan korunur).
-keep class com.digitoy.host.Net { *; }
