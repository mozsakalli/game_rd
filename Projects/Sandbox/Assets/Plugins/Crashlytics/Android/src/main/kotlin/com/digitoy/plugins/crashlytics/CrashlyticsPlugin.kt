package com.digitoy.plugins.crashlytics

import android.app.Application
import com.digitoy.host.DEPlugin
import com.google.firebase.crashlytics.FirebaseCrashlytics
import org.json.JSONObject

// Crashlytics host plugin'i (docs/plugins.md): core'un yakaladigi plugin hatalarini non-fatal olarak iletir (errorReporter),
// log/userId mesajlarini uygular. Unhandled managed exception icin kod yok: ManagedException'i Crashlytics kendisi yakalar.
class CrashlyticsPlugin : DEPlugin("Crashlytics") {
    override fun onApplicationCreate(app: Application) {
        DEPlugin.errorReporter = { t -> FirebaseCrashlytics.getInstance().recordException(t) }
    }

    override fun onMessage(msg: String) {
        val j = JSONObject(msg)
        when (j.optString("method")) {
            "log" -> FirebaseCrashlytics.getInstance().log(j.optString("message"))
            "userId" -> FirebaseCrashlytics.getInstance().setUserId(j.optString("id"))
        }
    }
}
