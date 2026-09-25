import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mfop_field/core/local_db.dart';
import 'package:mfop_field/core/secure_store.dart';
import 'package:sqlite3/sqlite3.dart';

void main() {
  test('the local database is encrypted at rest and opens only with its key', () async {
    final dir = Directory.systemTemp.createTempSync('mfop_db');
    final path = '${dir.path}/field.db';
    final store = MemorySecureStore();

    final db = await LocalDb.open(store, path: path);
    db.setValue('probe', 'بيانات ميدانية سرية');
    db.close();

    final raw = File(path).readAsBytesSync();
    expect(String.fromCharCodes(raw.take(15)), isNot('SQLite format 3'));
    expect(String.fromCharCodes(raw).contains('probe'), isFalse);

    expect(() => LocalDb.openWithKey('00' * 32, path: path), throwsA(isA<SqliteException>()));

    final reopened = await LocalDb.open(store, path: path);
    expect(reopened.getValue('probe'), 'بيانات ميدانية سرية');
    reopened.destroy();
    expect(File(path).existsSync(), isFalse);
    dir.deleteSync(recursive: true);
  });
}
