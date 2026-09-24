import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Small secrets that must stay inside the app sandbox, protected by Android Keystore:
/// the database key, the refresh token, the device id and the PIN hash.
abstract class SecureStore {
  Future<String?> read(String key);
  Future<void> write(String key, String value);
  Future<void> delete(String key);
  Future<void> deleteAll();
}

class KeystoreSecureStore implements SecureStore {
  KeystoreSecureStore() : _storage = const FlutterSecureStorage();

  final FlutterSecureStorage _storage;

  @override
  Future<String?> read(String key) => _storage.read(key: key);

  @override
  Future<void> write(String key, String value) => _storage.write(key: key, value: value);

  @override
  Future<void> delete(String key) => _storage.delete(key: key);

  /// Deletes only this app's own entries (flutter_secure_storage is scoped to the app).
  @override
  Future<void> deleteAll() => _storage.deleteAll();
}

class MemorySecureStore implements SecureStore {
  final Map<String, String> values = {};

  @override
  Future<String?> read(String key) async => values[key];

  @override
  Future<void> write(String key, String value) async => values[key] = value;

  @override
  Future<void> delete(String key) async => values.remove(key);

  @override
  Future<void> deleteAll() async => values.clear();
}

abstract final class SecureKeys {
  static const dbKey = 'db_key';
  static const refreshToken = 'refresh_token';
  static const deviceId = 'device_id';
  static const pinHash = 'pin_hash';
  static const privacyAccepted = 'privacy_accepted_v1';
}
