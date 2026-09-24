package com.reuxstudio.mfop.field

import android.os.Build
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
    }
}
