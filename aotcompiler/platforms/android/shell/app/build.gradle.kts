// Android Studio projesi kabugu (DigitoyEngine). Bu dosya ILK uretimde yazilir, sonra DOKUNULMAZ:
// bagimliliklar, SDK entegrasyonlari, ozel Gradle ayarlari burada elle/kancayla yonetilir.
// Oyun icerigi ../generated/ altindadir (her build yeniden yazilir):
//   cpp (C kaynaklari + CMake), java (host), assets (game.pak), res (launcher ikonu),
//   app.properties (Player Settings: applicationId, versionCode/Name, targetSdk, abis, appName, orientation).
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
        ndk { abiFilters += prop("abis", "arm64-v8a,x86_64").split(',').map { it.trim() } } // yalniz 64-bit (runtime 64-bit varsayar)
        manifestPlaceholders["appName"] = prop("appName")
        manifestPlaceholders["orientation"] = prop("orientation", "sensorLandscape")
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
        release {
            isMinifyEnabled = false
            signingConfig = if (hasReleaseSigning) signingConfigs.getByName("release") else signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
}

if (!hasReleaseSigning)
    logger.warn("DigitoyEngine: keystore.properties yok -> release DEBUG anahtariyla imzalanir (Player Settings > android.keystorePath / Android Signing)")
