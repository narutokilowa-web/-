package com.reuxstudio.mfop.field

import android.os.Build
import android.view.WindowManager
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        // Model and Android version for the device-registration request (shown to the approving supervisor).
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "mfop/device").setMethodCallHandler { call, result ->
            if (call.method == "describe") {
                result.success(mapOf("model" to "${Build.MANUFACTURER} ${Build.MODEL}", "osVersion" to "Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})"))
            } else {
                result.notImplemented()
            }
        }
        // FLAG_SECURE while evidence is on screen (docs/lean-pilot/05): no screenshots, blank in the recent-apps view.
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "mfop/secure").setMethodCallHandler { call, result ->
            if (call.method == "set") {
                if (call.arguments == true) window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
                else window.clearFlags(WindowManager.LayoutParams.FLAG_SECURE)
                result.success(null)
            } else {
                result.notImplemented()
            }
        }
    }
}
