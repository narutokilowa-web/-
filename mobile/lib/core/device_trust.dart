import 'package:flutter/services.dart';

/// The phone's monotonic clock: milliseconds since boot (not changed by setting the clock) and which boot this is.
class MonotonicTime {
  const MonotonicTime(this.elapsedMs, this.bootId);

  final int elapsedMs;
  final String bootId;
}

/// Device trust on the phone (docs/lean-pilot/05 §5.4, §5.7): the Keystore device key that signs write requests, the check
/// of the server's grant signature, and the monotonic clock. Only this app's own key is created or deleted.
abstract class DeviceTrust {
  /// EC P-256 public key (SubjectPublicKeyInfo, base64) of this installation's Keystore key; created on first use.
  Future<String> publicKey();

  /// ECDSA P-256 / SHA-256 signature (DER, base64) over [message] with the Keystore key.
  Future<String> sign(String message);

  /// Checks a signature made by the server's grant key ([publicKey]: SubjectPublicKeyInfo, base64).
  Future<bool> verify(String publicKey, String message, String signature);

  /// Deletes this app's own device key (revoked device, best-effort hygiene).
  Future<void> deleteKey();

  Future<MonotonicTime> now();
}

class PlatformDeviceTrust implements DeviceTrust {
  static const _channel = MethodChannel('mfop/trust');

  @override
  Future<String> publicKey() async => (await _channel.invokeMethod<String>('publicKey'))!;

  @override
  Future<String> sign(String message) async => (await _channel.invokeMethod<String>('sign', message))!;

  @override
  Future<bool> verify(String publicKey, String message, String signature) async =>
      await _channel.invokeMethod<bool>('verify', {'publicKey': publicKey, 'message': message, 'signature': signature}) ?? false;

  @override
  Future<void> deleteKey() => _channel.invokeMethod<void>('deleteKey');

  @override
  Future<MonotonicTime> now() async {
    final map = await _channel.invokeMapMethod<String, Object?>('clock') ?? const {};
    final bootCount = (map['bootCount'] as int?) ?? -1;
    return MonotonicTime((map['elapsedMs'] as int?) ?? 0, 'bc-$bootCount');
  }
}
