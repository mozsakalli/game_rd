pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
        // Plugin'lerin istedigi ek depolar (IAndroidProjectHook.MavenRepo) — editor her build yazar.
        val extra = file("generated/plugins.repos.txt")
        if (extra.exists()) extra.readLines().map { it.trim() }.filter { it.isNotEmpty() && !it.startsWith("#") }.forEach { maven { url = uri(it) } }
    }
}
rootProject.name = "{{APP_NAME}}"
include(":app")
// Plugin host API'si (DEPlugin/PluginLib) — editor her build generated/dehost'a yazar; plugin modulleri buna bagimli.
include(":dehost")
project(":dehost").projectDir = file("generated/dehost")
// Plugin Gradle library modulleri (Assets/Plugins/<Ad>/Android -> generated/plugins/<Ad>); editor her build yazar.
val pluginsSettings = file("generated/plugins.settings.gradle.kts")
if (pluginsSettings.exists()) apply(from = pluginsSettings)
