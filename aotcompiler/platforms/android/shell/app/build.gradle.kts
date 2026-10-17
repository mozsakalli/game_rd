// Android Studio projesi kabugu (DigitoyEngine). Bu dosya ILK uretimde yazilir, sonra DOKUNULMAZ:
// bagimliliklar, SDK entegrasyonlari, ozel Gradle ayarlari burada elle/kancayla yonetilir.
// Oyun icerigi ../generated/ altindadir (her build yeniden yazilir):
//   cpp (C kaynaklari + CMake), java (host), assets (game.pak), res (launcher ikonu),
//   app.properties (Player Settings: applicationId, versionCode/Name, targetSdk, abis, debugAbis, appName, orientation).
// Imza: ../keystore.properties varsa (editor Player Settings > android.keystorePath + Android Signing) release
// onunla imzalanir; yoksa debug anahtari (yalniz test; Play'e yuklenemez).
import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

val appProps = Properties().apply {
    val f = rootProject.file("generated/app.properties")
    if (!f.exists()) throw GradleException("generated/app.properties yok — editorden Project > Build Android Project calistirin")
    f.inputStream().use { load(it) }
}
fun prop(key: String, default: String? = null): String =
    appProps.getProperty(key) ?: default ?: throw GradleException("generated/app.properties: '$key' eksik")

val keystoreProps = Properties().apply {
    val f = rootProject.file("keystore.properties")
    if (f.exists()) f.inputStream().use { load(it) }
}
val hasReleaseSigning = keystoreProps.getProperty("storeFile")?.isNotBlank() == true
val targetSdkVersion = prop("targetSdk", "35").toInt()

android {
    namespace = "com.digitoy.host" // host Kotlin paketi (R sinifi); applicationId ayri
    compileSdk = maxOf(35, targetSdkVersion)

    defaultConfig {
        applicationId = prop("applicationId")
        minSdk = 26 // AAudio
        targetSdk = targetSdkVersion
        versionCode = prop("versionCode").toInt()
        versionName = prop("versionName")
        // ABI: yalniz 64-bit (runtime 64-bit varsayar). abis = release/store listesi (varsayilan yalniz arm64-v8a);
        // AGP buildType abiFilters'i defaultConfig ile BIRLESTIRIR (cikarma yok) -> emulator ABI'si debug'a asagida eklenir.
        ndk { abiFilters += prop("abis", "arm64-v8a").split(',').map { it.trim() }.filter { it.isNotEmpty() } }
        manifestPlaceholders["appName"] = prop("appName")
        manifestPlaceholders["orientation"] = prop("orientation", "sensorLandscape")
        // Plugin kancalarinin placeholder'lari (placeholder.<ad>=<deger>).
        for (k in appProps.stringPropertyNames()) if (k.startsWith("placeholder.")) manifestPlaceholders[k.removePrefix("placeholder.")] = appProps.getProperty(k)
        externalNativeBuild {
            cmake {
                arguments += listOf("-DANDROID_STL=none") // saf C
            }
        }
    }

    externalNativeBuild {
        cmake {
            path = file("../generated/cpp/CMakeLists.txt")
            version = "3.22.1"
        }
    }

    sourceSets {
        getByName("main") {
            java.srcDirs("../generated/java")
            assets.srcDirs("../generated/assets")
            res.srcDirs("../generated/res")
            // Plugin manifest parcalari (izin/meta-data) library modullerinde; app manifest'i statik kalir.
        }
    }

    // game.pak sikistirilmadan APK'ya girer (AssetFileDescriptor ile dogrudan uzunluk/kopya).
    androidResources { noCompress += "pak" }

    signingConfigs {
        if (hasReleaseSigning) {
            create("release") {
                storeFile = file(keystoreProps.getProperty("storeFile"))
                storePassword = keystoreProps.getProperty("storePassword")
                keyAlias = keystoreProps.getProperty("keyAlias")
                keyPassword = keystoreProps.getProperty("keyPassword") ?: keystoreProps.getProperty("storePassword")
            }
        }
    }

    buildTypes {
        debug {
            // Emulator ABI'si (x86_64) yalniz debug APK'ya girer; release'e ASLA eklenmez.
            ndk { abiFilters += prop("debugAbis", "x86_64").split(',').map { it.trim() }.filter { it.isNotEmpty() } }
            isMinifyEnabled = false
        }
        release {
            // R8: SDK plugin'leri (reklam/login/firebase) kendi consumer-rules.pro'sunu tasir; bizim keep'ler proguard-rules.pro
            // (JNI'den isimle cagrilan host siniflari). Trim platformun isi (docs/plugins.md).
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfig = if (hasReleaseSigning) signingConfigs.getByName("release") else signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
}

dependencies {
    implementation(project(":dehost")) // DEPlugin/PluginLib (+ androidx.fragment api ile gelir)
}

// Plugin modulleri + app-seviyesi gradle plugin'leri + surum sabitlemeleri (IAndroidProjectHook) — editor her build yazar.
val pluginsDeps = rootProject.file("generated/plugins.deps.gradle.kts")
if (pluginsDeps.exists()) apply(from = pluginsDeps)

if (!hasReleaseSigning)
    logger.warn("DigitoyEngine: keystore.properties yok -> release DEBUG anahtariyla imzalanir (Player Settings > android.keystorePath / Android Signing)")
