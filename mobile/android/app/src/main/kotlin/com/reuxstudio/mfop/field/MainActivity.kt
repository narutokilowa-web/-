package com.reuxstudio.mfop.field

import android.os.Build
import android.os.SystemClock
import android.provider.Settings
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import android.view.WindowManager
import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.Signature
import java.security.spec.ECGenParameterSpec
import java.security.spec.X509EncodedKeySpec
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
        // M4 device trust (docs/lean-pilot/05 §5.4, §5.7): a non-exportable EC P-256 key in the Android Keystore signs every
        // write request; the server's grant signature is checked here; the monotonic clock and boot count place offline work
        // on the server's clock. Only this app's own key is ever created or deleted — nothing else on the phone is touched.
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "mfop/trust").setMethodCallHandler { call, result ->
            try {
                when (call.method) {
                    "publicKey" -> result.success(Base64.encodeToString(deviceKey().second, Base64.NO_WRAP))
                    "sign" -> {
                        val signer = Signature.getInstance("SHA256withECDSA")
                        signer.initSign(deviceKey().first)
                        signer.update((call.arguments as String).toByteArray(Charsets.UTF_8))
                        result.success(Base64.encodeToString(signer.sign(), Base64.NO_WRAP))
                    }
                    "verify" -> {
                        val args = call.arguments as Map<*, *>
                        val publicKey = KeyFactory.getInstance("EC").generatePublic(X509EncodedKeySpec(Base64.decode(args["publicKey"] as String, Base64.NO_WRAP)))
                        val verifier = Signature.getInstance("SHA256withECDSA")
                        verifier.initVerify(publicKey)
                        verifier.update((args["message"] as String).toByteArray(Charsets.UTF_8))
                        result.success(verifier.verify(Base64.decode(args["signature"] as String, Base64.NO_WRAP)))
                    }
                    "deleteKey" -> {
                        keyStore().deleteEntry(KEY_ALIAS)
                        result.success(null)
                    }
                    "clock" -> {
                        val bootCount = try { Settings.Global.getInt(contentResolver, Settings.Global.BOOT_COUNT) } catch (e: Settings.SettingNotFoundException) { -1 }
                        result.success(mapOf("elapsedMs" to SystemClock.elapsedRealtime(), "bootCount" to bootCount))
                    }
                    else -> result.notImplemented()
                }
            } catch (e: Exception) {
                result.error("TRUST_ERROR", e.javaClass.simpleName + ": " + (e.message ?: ""), null)
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

    private fun keyStore(): KeyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

    /** The device key (created once, never exported): private key handle and its SubjectPublicKeyInfo. */
    private fun deviceKey(): Pair<PrivateKey, ByteArray> {
        val store = keyStore()
        if (!store.containsAlias(KEY_ALIAS)) {
            val generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, "AndroidKeyStore")
            generator.initialize(
                KeyGenParameterSpec.Builder(KEY_ALIAS, KeyProperties.PURPOSE_SIGN)
                    .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
                    .setDigests(KeyProperties.DIGEST_SHA256)
                    .build()
            )
            generator.generateKeyPair()
        }
        val entry = store.getEntry(KEY_ALIAS, null) as KeyStore.PrivateKeyEntry
        return Pair(entry.privateKey, entry.certificate.publicKey.encoded)
    }

    companion object {
        private const val KEY_ALIAS = "mfop_device_key_v1"
    }
}
