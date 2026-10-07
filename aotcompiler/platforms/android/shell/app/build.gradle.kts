// Android Studio projesi kabugu (DigitoyEngine). Bu dosya ILK uretimde yazilir, sonra DOKUNULMAZ:
// imza, surum, bagimliliklar, SDK entegrasyonlari burada elle/kancayla yonetilir.
// Oyun icerigi ../generated/ altindadir (her build yeniden yazilir): cpp (C kaynaklari + CMake), java (host), assets (game.pak).
plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "{{APP_ID}}"
    compileSdk = 35

    defaultConfig {
        applicationId = "{{APP_ID}}"
        minSdk = 26 // AAudio
        targetSdk = 35
        versionCode = 1
        versionName = "{{APP_VERSION}}"
        ndk { abiFilters += listOf("arm64-v8a", "x86_64") } // yalniz 64-bit (runtime 64-bit varsayar)
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
        }
    }

    // game.pak sikistirilmadan APK'ya girer (AssetFileDescriptor ile dogrudan uzunluk/kopya).
    androidResources { noCompress += "pak" }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("debug") // TODO: kendi imzaniz
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
}
