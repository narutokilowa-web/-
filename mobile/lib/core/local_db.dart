import 'dart:io';
import 'dart:math';

import 'package:sqlite3/sqlite3.dart';

import 'secure_store.dart';

/// Encrypted local database (docs/lean-pilot/05 §5.8). SQLite3 Multiple Ciphers with the SQLCipher v4 scheme;
/// the random 256-bit key lives in Keystore-backed secure storage, never in the database or the code.
class LocalDb {
  LocalDb._(this.db, this.path);

  final Database db;

  /// Null for an in-memory database (tests).
  final String? path;

  static Future<LocalDb> open(SecureStore store, {String? path}) async {
    var key = await store.read(SecureKeys.dbKey);
    if (key == null) {
      final random = Random.secure();
      key = List.generate(32, (_) => random.nextInt(256).toRadixString(16).padLeft(2, '0')).join();
      await store.write(SecureKeys.dbKey, key);
    }

    return openWithKey(key, path: path);
  }

  static LocalDb openWithKey(String hexKey, {String? path}) {
    if (path == null) {
      // In-memory (tests only): never written to disk, and SQLite3MC cannot key an in-memory database.
      final memory = sqlite3.openInMemory();
      _migrate(memory);
      return LocalDb._(memory, null);
    }

    final db = sqlite3.open(path);
    // Raw 256-bit key (no passphrase KDF needed: the key is random, not a password).
    db.execute("PRAGMA cipher = 'sqlcipher'; PRAGMA legacy = 4; PRAGMA hexkey = '$hexKey';");
    // Fails here (SqliteException) if the key does not match the file.
    db.execute('SELECT count(*) FROM sqlite_master;');
    _migrate(db);
    return LocalDb._(db, path);
  }

  static void _migrate(Database db) {
    db.execute('''
      CREATE TABLE IF NOT EXISTS outbox (
        command_id TEXT PRIMARY KEY,
        seq INTEGER NOT NULL,
        type TEXT NOT NULL,
        payload TEXT NOT NULL,
        created_at TEXT NOT NULL,
        state TEXT NOT NULL,
        attempts INTEGER NOT NULL DEFAULT 0,
        result_code TEXT,
        result_message TEXT,
        entity_id TEXT
      );
      CREATE TABLE IF NOT EXISTS kv (key TEXT PRIMARY KEY, value TEXT NOT NULL);
      -- M3: photos waiting for upload. The JPEG bytes stay inside this encrypted database (not loose files) and are
      -- deleted as soon as the server has received them.
      CREATE TABLE IF NOT EXISTS evidence (
        evidence_id TEXT PRIMARY KEY,
        work_order_id TEXT NOT NULL,
        kind TEXT NOT NULL,
        sha256 TEXT NOT NULL,
        size_bytes INTEGER NOT NULL,
        captured_at TEXT NOT NULL,
        command_id TEXT NOT NULL,
        state TEXT NOT NULL,
        attempts INTEGER NOT NULL DEFAULT 0,
        error TEXT,
        bytes BLOB
      );
    ''');
  }

  String? getValue(String key) {
    final rows = db.select('SELECT value FROM kv WHERE key = ?', [key]);
    return rows.isEmpty ? null : rows.first['value'] as String;
  }

  void setValue(String key, String value) =>
      db.execute('INSERT INTO kv(key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value', [key, value]);

  void close() => db.close();

  /// Revoked-device hygiene: closes and deletes this app's own database file. Touches nothing outside the app sandbox.
  void destroy() {
    db.close();
    if (path != null) {
      for (final suffix in ['', '-wal', '-shm', '-journal']) {
        final f = File('$path$suffix');
        if (f.existsSync()) f.deleteSync();
      }
    }
  }
}
