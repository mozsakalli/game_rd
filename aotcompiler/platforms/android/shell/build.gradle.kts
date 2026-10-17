// DigitoyEngine Android kabugu (ilk uretimde yazilir, sonra dokunulmaz). Degisken ayarlar generated/app.properties'te.
// Plugin'lerin app-seviyesi Gradle plugin'leri (google-services, crashlytics...) generated/gradle-plugins.properties'ten
// (id=version; editor her build yazar) classpath'e eklenir; app modulu apply(plugin=) ile uygular.
buildscript {
    repositories { google(); mavenCentral(); gradlePluginPortal() }
    dependencies {
        val f = rootProject.file("generated/gradle-plugins.properties")
        if (f.exists()) {
            val p = java.util.Properties().apply { f.inputStream().use { load(it) } }
            for (id in p.stringPropertyNames()) classpath("$id:$id.gradle.plugin:${p.getProperty(id)}")
        }
    }
}
plugins {
    id("com.android.application") version "8.7.3" apply false
    id("org.jetbrains.kotlin.android") version "2.0.21" apply false
}
