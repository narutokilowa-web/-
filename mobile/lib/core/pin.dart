import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'package:crypto/crypto.dart';

import 'secure_store.dart';

/// App PIN (docs/lean-pilot/05 §5.4): protects against family or shoulder access. Not a server credential.
/// Stored as PBKDF2-HMAC-SHA256(salt, pin) in Keystore-backed storage. After [maxAttempts] wrong entries the
/// user must sign in again (the refresh token is forgotten), which needs the server and the password.
class PinService {
  PinService(this._store, {this.iterations = 60000});

  final SecureStore _store;
  final int iterations;
  int _failures = 0;

  static const maxAttempts = 5;
  static final _digits = RegExp(r'^\d{4,8}$');

  static bool isValidFormat(String pin) => _digits.hasMatch(pin);

  Future<bool> isSet() async => await _store.read(SecureKeys.pinHash) != null;

  int get failures => _failures;

  Future<void> set(String pin) async {
    if (!isValidFormat(pin)) throw ArgumentError('PIN must be 4 to 8 digits.');
    final salt = Uint8List.fromList(List.generate(16, (_) => Random.secure().nextInt(256)));
    await _store.write(SecureKeys.pinHash, '${base64Encode(salt)}:${base64Encode(_pbkdf2(pin, salt))}');
    _failures = 0;
  }

  /// Returns true when the PIN matches. Wrong entries count towards [maxAttempts].
  Future<bool> verify(String pin) async {
    final stored = await _store.read(SecureKeys.pinHash);
    if (stored == null) return false;
    final parts = stored.split(':');
    final ok = _equal(_pbkdf2(pin, base64Decode(parts[0])), base64Decode(parts[1]));
    _failures = ok ? 0 : _failures + 1;
    return ok;
  }

  bool get exhausted => _failures >= maxAttempts;

  Future<void> clear() async {
    _failures = 0;
    await _store.delete(SecureKeys.pinHash);
  }

  Uint8List _pbkdf2(String pin, List<int> salt) {
    final hmac = Hmac(sha256, utf8.encode(pin));
    var u = hmac.convert([...salt, 0, 0, 0, 1]).bytes;
    final result = Uint8List.fromList(u);
    for (var i = 1; i < iterations; i++) {
      u = hmac.convert(u).bytes;
      for (var j = 0; j < result.length; j++) {
        result[j] ^= u[j];
      }
    }

    return result;
  }

  static bool _equal(List<int> a, List<int> b) {
    if (a.length != b.length) return false;
    var diff = 0;
    for (var i = 0; i < a.length; i++) {
      diff |= a[i] ^ b[i];
    }

    return diff == 0;
  }
}
