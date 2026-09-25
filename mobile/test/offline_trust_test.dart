import 'dart:convert';

import 'package:crypto/crypto.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mfop_field/app/app_controller.dart';
import 'package:mfop_field/core/photo_processing.dart';
import 'package:mfop_field/core/secure_store.dart';

import 'support.dart';

/// M4 on the phone (docs/lean-pilot/05 §5.4–5.7): device-key signing, the offline grant and its expiry on the monotonic
/// clock, locked states, conflicts and held commands, resumable uploads, reconnect, and revocation of the employee.
Future<Harness> ready({DateTime Function()? clock, int chunkSize = 256 * 1024, Map<String, dynamic>? task}) async {
  final h = Harness(clock: clock, chunkSize: chunkSize);
  await h.store.write(SecureKeys.privacyAccepted, 'yes');
  await h.auth.signIn();
  h.server.deviceStatus = 'ACTIVE';
  h.server.tasks = [task ?? {...taskJson(), 'progress': progressJson(), 'eventSeq': 7}];
  await h.controller.start();
  await h.controller.setPin('2468');
  return h;
}

List<Map<String, dynamic>> pushed(Harness h) => [
  for (final r in h.server.requests.where((r) => r.url.path == '/sync/v1/push'))
    for (final c in (jsonDecode(r.body) as Map<String, dynamic>)['commands'] as List<dynamic>) c as Map<String, dynamic>,
];

void main() {
  test('the device key is registered, writes are signed, and every request carries the app version and the monotonic clock', () async {
    final h = Harness();
    await h.store.write(SecureKeys.privacyAccepted, 'yes');
    await h.controller.start();
    await h.controller.signIn();
    final register = h.server.requests.singleWhere((r) => r.url.path == '/sync/v1/devices/register');
    expect(jsonDecode(register.body)['publicKey'], 'device-public-key');
    expect(jsonDecode(register.body)['appVersion'], '0.4.0');
    expect(register.headers['X-App-Version'], '0.4.0');

    h.server.deviceStatus = 'ACTIVE';
    h.server.tasks = [{...taskJson(), 'progress': progressJson()}];
    await h.controller.refreshDeviceStatus();
    await h.controller.setPin('2468');
    await h.controller.arrive(h.controller.tasks.single, scanned: '7K3M92QX.4F');
    await h.controller.syncNow();

    for (final path in ['/sync/v1/push', '/sync/v1/grant']) {
      final r = h.server.requests.firstWhere((r) => r.url.path == path);
      final message = 'POST\n$path\n${r.headers['X-Signature-Timestamp']}\n${r.headers['X-Signature-Nonce']}\n${sha256.convert(utf8.encode(r.body))}';
      expect(h.trust.signed, contains(message), reason: '$path is signed over method, path, time, nonce and body hash');
      expect(r.headers['X-Device-Signature'], startsWith('signature-'));
    }

    final pull = h.server.requests.lastWhere((r) => r.url.path == '/sync/v1/pull');
    expect(pull.headers['X-Device-Elapsed-Ms'], '${h.trust.elapsedMs}');
    expect(pull.headers['X-Device-Boot-Id'], 'boot-1');
    expect(pull.headers.containsKey('X-Device-Signature'), isFalse);
  });

  test('each command carries the grant, boot and monotonic clock it was made under and the version the technician saw', () async {
    final h = await ready();
    final c = h.controller;
    expect(c.grants!.current!.grantId, 'grant-1');
    h.trust.advance(const Duration(minutes: 3));
    await c.arrive(c.tasks.single, scanned: '7K3M92QX.4F');
    await c.syncNow();
    final sent = pushed(h).single;
    expect(sent['grantId'], 'grant-1');
    expect(sent['elapsedMs'], h.trust.elapsedMs);
    expect(sent['bootId'], 'boot-1');
    expect(sent['baseVersion'], 7);
    expect(c.grants!.current!.grantId, isNot('grant-1'), reason: 'renewed on every successful sync, after sending');
  });

  test('offline past the grant: new work is refused and the app locks; the outbox is kept and sent after reconnecting', () async {
    var wall = DateTime.utc(2026, 9, 24, 10, 5);
    final h = await ready(clock: () => wall);
    final c = h.controller;
    final task = c.tasks.single;

    h.server.offline = true;
    await c.arrive(task, scanned: '7K3M92QX.4F');
    expect(c.outbox!.pendingCount, 1);

    // 25 hours later on the phone's monotonic clock; moving the phone's clock back does not help.
    h.trust.advance(const Duration(hours: 25));
    wall = DateTime.utc(2026, 9, 24, 9);
    expect(await c.grantValid(), isFalse);
    await c.recordDiagnosis(task, 'LED_MODULE_FAIL', null);
    expect(c.stage, AppStage.grantExpired);
    expect(c.outbox!.pendingCount, 1, reason: 'nothing new was recorded; what was recorded is kept');

    // Back online: the outbox goes first (made under the old grant), then a new grant unlocks the app.
    h.server.offline = false;
    h.server.serverTime = h.server.serverTime.add(const Duration(hours: 25));
    await c.retryConnection();
    expect(c.stage, AppStage.ready);
    expect(pushed(h).single['grantId'], 'grant-1');
    expect(c.outbox!.pendingCount, 0);
    expect(c.grants!.current!.grantId, isNot('grant-1'));
  });

  test('after a restart offline, expiry falls back to the wall clock but never earlier than the last server time seen', () async {
    var wall = DateTime.utc(2026, 9, 24, 10, 5);
    final h = await ready(clock: () => wall);
    h.server.offline = true;
    h.trust
      ..bootId = 'boot-2'
      ..elapsedMs = 5000;
    wall = DateTime.utc(2026, 9, 26); // 2 days later: expired
    expect(await h.controller.grantValid(), isFalse);
    wall = DateTime.utc(2026, 9, 20); // clock set back before the grant: still judged from the last trusted server time
    expect(await h.controller.grantValid(), isTrue, reason: 'floor = last server time (10:05), inside the 24 h grant');
    wall = DateTime.utc(2026, 9, 24, 12);
    expect(await h.controller.grantValid(), isTrue);
  });

  test('a grant whose server signature does not verify, or signed with a different key for a known key id, is not used', () async {
    final h = await ready();
    final c = h.controller;
    expect(c.grants!.pinnedKeys, {'k1': 'server-grant-key'});

    h.trust.verifyResult = false;
    await c.syncNow();
    expect(c.message, 'GRANT_BADSIGNATURE');
    expect(c.grants!.current!.grantId, 'grant-1');

    h.trust.verifyResult = true;
    h.server.grantKey = 'someone-elses-key';
    await c.syncNow();
    expect(c.message, 'GRANT_KEYCHANGED');
    expect(c.grants!.current!.grantId, 'grant-1');
  });

  test('the grant lifetime and app lock come from the server policy, not the app', () async {
    final h = Harness();
    h.server.grantHours = 6;
    await h.store.write(SecureKeys.privacyAccepted, 'yes');
    await h.auth.signIn();
    h.server.deviceStatus = 'ACTIVE';
    await h.controller.start();
    await h.controller.setPin('2468');
    final g = h.controller.grants!.current!;
    expect(g.expiresAt.difference(g.issuedAt), const Duration(hours: 6));
    expect(h.controller.appLockMinutes, 5);
  });

  test('426 from the server: the app shows update-required and recovers once the version is accepted', () async {
    final h = await ready();
    h.server.minAppVersion = '0.5.0';
    await h.controller.syncNow();
    expect(h.controller.stage, AppStage.updateRequired);
    expect(h.controller.minAppVersion, '0.5.0');
    h.server.minAppVersion = null;
    await h.controller.retryConnection();
    expect(h.controller.stage, AppStage.locked);
  });

  test('a disabled employee: the app deletes only its own data and its own device key', () async {
    final h = await ready();
    h.controller.outbox!.enqueue('ReportFault', {'x': 1});
    h.server.blockedCode = 'USER_DISABLED';
    await h.controller.syncNow();
    expect(h.controller.stage, AppStage.deviceRevoked);
    expect(h.controller.blockedCode, 'USER_DISABLED');
    expect(h.trust.keyDeleted, isTrue);
    expect(h.store.values.keys, [SecureKeys.dbKey]);
    expect(h.controller.outbox!.pendingCount, 0);
  });

  test('a refused change and a held command stay visible; a held command learns the supervisor decision on a later sync', () async {
    final h = await ready();
    final c = h.controller;
    final task = c.tasks.single;
    await c.arrive(task, scanned: '7K3M92QX.4F');
    await c.recordDiagnosis(task, 'LED_MODULE_FAIL', null);
    h.server.pushResults = (commands) => [
      {'commandId': (commands[0] as Map)['commandId'], 'result': 'QUARANTINED', 'code': 'WORK_ORDER_CANCELLED', 'message': 'x'},
      if (commands.length > 1) {'commandId': (commands[1] as Map)['commandId'], 'result': 'REJECTED', 'code': 'WORK_ORDER_CHANGED', 'message': 'x'},
    ];
    await c.syncNow();
    final view = c.workView(task);
    expect(view.held.single.code, 'WORK_ORDER_CANCELLED');
    expect(view.rejections.single.code, 'WORK_ORDER_CHANGED');
    expect(c.syncStatus!.held, 1);

    h.server.pushResults = (commands) => [
      for (final x in commands) {'commandId': (x as Map)['commandId'], 'result': 'DUPLICATE', 'code': null, 'message': null},
    ];
    await c.syncNow();
    expect(c.workView(task).held, isEmpty);
    expect(c.syncStatus!.held, 0);
  });

  test('a photo upload cut off mid-way resumes from the bytes the server already has', () async {
    final h = await ready(chunkSize: 1024);
    final c = h.controller;
    final task = c.tasks.single;
    await c.arrive(task, scanned: '7K3M92QX.4F');
    final photo = processPhoto(PhotoRequest(cameraJpeg(width: 400, height: 300, seed: 5), 'WO BEFORE'));
    expect(photo.bytes.length, greaterThan(3000));
    h.server.failUploadAfterBytes = 2500;
    await c.addPhoto(task, 'BEFORE', photo, DateTime.utc(2026, 9, 25));
    await c.syncNow();
    expect(c.sync!.evidence.waitingCount, 1, reason: 'the connection dropped during the third chunk');
    expect(h.server.partial.values.single.length, 2500);

    await c.syncNow();
    expect(c.sync!.evidence.waitingCount, 0);
    expect(h.server.uploads.values.single, photo.bytes);
    final ranges = h.server.requests.where((r) => r.method == 'PUT').map((r) => r.headers['Content-Range']).toList();
    expect(ranges.last, endsWith('-${photo.bytes.length - 1}/${photo.bytes.length}'));
    expect(ranges, contains('bytes 2500-3523/${photo.bytes.length}'), reason: 'resumed at the server offset, not at 0');
  });

  test('without network the app retries with a growing delay, and stops once back online', () async {
    final h = await ready();
    final c = h.controller;
    h.server.offline = true;
    await c.syncNow();
    expect(c.retryDelay, AppController.firstRetry);
    await c.syncNow();
    expect(c.retryDelay, AppController.firstRetry * 2);
    h.server.offline = false;
    await c.syncNow();
    expect(c.retryDelay, isNull);
  });
}
