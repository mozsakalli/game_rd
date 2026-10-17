// :dehost — plugin host API'si (DEPlugin, PluginLib). Plugin library modulleri implementation(project(":dehost")) ile derlenir;
// app da buna bagimli. aotcompiler generated/dehost'a her build yazar (docs/plugins.md).
plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
}
android {
    namespace = "com.digitoy.hostapi"
    compileSdk = 35
    defaultConfig { minSdk = 26 }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
    sourceSets["main"].java.srcDirs("src/main/kotlin")
}
dependencies {
    api("androidx.fragment:fragment:1.8.5")
}
