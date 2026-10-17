package com.digitoy.host

// Yakalanmayan managed (C#) exception'in Java karsiligi (docs/plugins.md "de_host_fatal"): libgame.so dispatch'te
// yakalayan yoksa host_android.c bu siniftan bir nesne kurar, stack trace'ini .cs frame'leriyle (Tip.Metot(Dosya.cs:satir))
// doldurur ve JNI'den firlatir -> ART uncaught -> surec olur -> Play vitals / Crashlytics raporunda SATIR BILGILI managed
// trace gorunur. (exit() reporter'lara gorunmezdi.) Bu dosya her build'de yeniden yazilir; proguard-rules.pro keep eder.
class ManagedException(message: String) : RuntimeException(message)
