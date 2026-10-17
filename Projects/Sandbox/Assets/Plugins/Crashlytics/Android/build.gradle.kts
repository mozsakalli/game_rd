// Crashlytics plugin — Android Gradle LIBRARY modulu (docs/plugins.md). Firebase BoM ile surum; google-services/crashlytics
// gradle plugin'leri app seviyesinde (CrashlyticsAndroid kancasi). Unhandled managed exception = Java ManagedException (host).
plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.digitoy.plugins.crashlytics"
    compileSdk = 35
    defaultConfig { minSdk = 26 }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
    sourceSets["main"].java.srcDirs("src/main/kotlin")
}

dependencies {
    implementation(project(":dehost"))
    implementation(platform("com.google.firebase:firebase-bom:33.1.0"))
    implementation("com.google.firebase:firebase-crashlytics")
}
