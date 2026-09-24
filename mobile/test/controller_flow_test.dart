import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mfop_field/app/app_controller.dart';
import 'package:mfop_field/core/secure_store.dart';

import 'support.dart';

void main() {
  test('first run: privacy → sign in → device registered and waiting → approved → PIN → tasks', () async {
    final h = Harness();
    final c = h.controller;
    await c.start();
    expect(c.stage, AppStage.privacy);

    await c.acceptPrivacy();
    expect(c.stage, AppStage.signedOut);

    await c.signIn();
    expect(c.stage, AppStage.waitingApproval);
    expect(await h.store.read(SecureKeys.deviceId), 'device-1');
    final register = h.server.requests.singleWhere((r) => r.url.path == '/sync/v1/devices/register');
    expect(register.body, contains('Test Phone'));
    expect(register.headers.containsKey('X-Device-Id'), isFalse);

    h.server.deviceStatus = 'ACTIVE';
    h.server.tasks = [taskJson(code: 'WO-2026-000002', priority: 'P3'), taskJson(code: 'WO-2026-000001', priority: 'P1')];
    await c.refreshDeviceStatus();
    expect(c.stage, AppStage.setPin);

    await c.setPin('2468');
    expect(c.stage, AppStage.ready);
    expect(c.tasks.map((t) => t.code), ['WO-2026-000002', 'WO-2026-000001']); // server order kept (server sorts)
    expect(c.isTechnician, isTrue);
    expect(c.userName, 'فني تجريبي');
    final pull = h.server.requests.lastWhere((r) => r.url.path == '/sync/v1/pull');
    expect(pull.headers['X-Device-Id'], 'device-1');
    expect(pull.headers['Authorization'], 'Bearer access');
  });

  test('a rejected registration can be sent again', () async {
    final h = Harness();
    await h.store.write(SecureKeys.privacyAccepted, 'yes');
    await h.auth.signIn();
    h.server.deviceStatus = 'REJECTED';
    await h.controller.start();
    expect(h.controller.stage, AppStage.deviceRejected);
    expect(await h.store.read(SecureKeys.deviceId), isNull);

    h.server.deviceStatus = 'PENDING_APPROVAL';
    await h.controller.refreshDeviceStatus();
    expect(h.controller.stage, AppStage.waitingApproval);
  });

  test('locks after the configured minutes in the background and unlocks with the PIN; too many wrong PINs need a new sign-in', () async {
    var now = DateTime.utc(2026, 9, 24, 10);
    final h = Harness(clock: () => now);
    final c = h.controller;
    await h.store.write(SecureKeys.privacyAccepted, 'yes');
    await h.auth.signIn();
    h.server.deviceStatus = 'ACTIVE';
    await c.start();
    await c.setPin('1357');
    expect(c.stage, AppStage.ready);

    c.didChangeAppLifecycleState(AppLifecycleState.paused);
    now = now.add(const Duration(minutes: 2));
    c.didChangeAppLifecycleState(AppLifecycleState.resumed);
    expect(c.stage, AppStage.ready, reason: 'short absence: no lock');

    c.didChangeAppLifecycleState(AppLifecycleState.paused);
    now = now.add(const Duration(minutes: 5));
    c.didChangeAppLifecycleState(AppLifecycleState.resumed);
    expect(c.stage, AppStage.locked);

    expect(await c.unlock('0000'), isFalse);
    expect(await c.unlock('1357'), isTrue);
    expect(c.stage, AppStage.ready);

    c.lock();
    for (var i = 0; i < 5; i++) {
      await c.unlock('9999');
    }

    expect(c.stage, AppStage.signedOut);
    expect(await h.store.read(SecureKeys.refreshToken), isNull);
  });

  test('revoked device: the server refuses it and the app deletes only its own data', () async {
    final h = Harness();
    final c = h.controller;
    await h.store.write(SecureKeys.privacyAccepted, 'yes');
    await h.auth.signIn();
    h.server.deviceStatus = 'ACTIVE';
    h.server.tasks = [taskJson()];
    await c.start();
    await c.setPin('2468');
    expect(c.tasks, hasLength(1));
    c.outbox!.enqueue('ReportFault', {'x': 1});

    final oldKey = h.store.values[SecureKeys.dbKey];
    h.server.blockedCode = 'DEVICE_REVOKED';
    await c.syncNow();

    expect(c.stage, AppStage.deviceRevoked);
    // Refresh token, device id, PIN and the old database key are gone; only a NEW key for a new, empty database exists.
    expect(h.store.values.keys, [SecureKeys.dbKey]);
    expect(h.store.values[SecureKeys.dbKey], isNot(oldKey));
    expect(c.tasks, isEmpty);
    expect(c.outbox!.pendingCount, 0, reason: 'the old encrypted database was destroyed');
  });

  test('offline: tasks stay available from the encrypted cache and the strip shows offline', () async {
    final h = Harness();
    final c = h.controller;
    await h.store.write(SecureKeys.privacyAccepted, 'yes');
    await h.auth.signIn();
    h.server.deviceStatus = 'ACTIVE';
    h.server.tasks = [taskJson()];
    await c.start();
    await c.setPin('2468');

    h.server.offline = true;
    final status = await c.syncNow();
    expect(status!.online, isFalse);
    expect(c.tasks.single.code, 'WO-2026-000001');
  });

  test('scan: QR URL or typed code resolves to the asset card; unknown or malformed codes do not', () async {
    final h = Harness();
    final c = h.controller;
    await h.store.write(SecureKeys.privacyAccepted, 'yes');
    await h.auth.signIn();
    h.server.deviceStatus = 'ACTIVE';
    h.server.tasks = [taskJson()];
    await c.start();

    final card = await c.resolveTag('http://localhost:5173/t/7K3M92QX.4F');
    expect(card!.asset.assetCode, 'SL-000042');
    expect(card.myOpenTasks.single.code, 'WO-2026-000001');
    expect((await c.resolveTag('7k3m92qx4f'))!.openFaults, 1);
    expect(await c.resolveTag('hello'), isNull);
    await expectLater(c.resolveTag('7K3M92QX.ZZ'), throwsA(anything));
  });
}
