import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:mfop_field/core/api_client.dart';
import 'package:mfop_field/core/local_db.dart';
import 'package:mfop_field/core/outbox.dart';
import 'package:mfop_field/core/secure_store.dart';
import 'package:mfop_field/core/sync_engine.dart';

import 'support.dart';

void main() {
  late FakeServer server;
  late Outbox outbox;
  late SyncEngine sync;

  setUp(() async {
    final store = MemorySecureStore();
    await store.write(SecureKeys.refreshToken, 'r');
    await store.write(SecureKeys.deviceId, 'device-1');
    server = FakeServer()..deviceStatus = 'ACTIVE';
    final db = await LocalDb.open(store);
    final api = ApiClient(testConfig, FakeAuth(store), () => store.read(SecureKeys.deviceId), httpClient: server.client);
    outbox = Outbox(db);
    sync = SyncEngine(api, outbox, db);
  });

  test('commands are pushed in order with stable ids, and results are recorded per command', () async {
    final a = outbox.enqueue('ReportFault', {'n': 1});
    final b = outbox.enqueue('ReportFault', {'n': 2});
    final c = outbox.enqueue('LaunchRocket', {'n': 3});
    server.pushResults = (commands) => [
      {'commandId': a, 'result': 'APPLIED', 'code': 'CREATED', 'entityId': 'f1'},
      {'commandId': b, 'result': 'DUPLICATE', 'code': null, 'entityId': 'f1'},
      {'commandId': c, 'result': 'REJECTED', 'code': 'UNKNOWN_COMMAND', 'message': 'no'},
    ];

    await sync.syncNow();

    final pushed = jsonDecode(server.requests.firstWhere((r) => r.url.path == '/sync/v1/push').body) as Map<String, dynamic>;
    expect((pushed['commands'] as List<dynamic>).map((x) => (x as Map)['commandId']), [a, b, c]);
    expect(outbox.pendingCount, 0);
    final items = {for (final i in outbox.all()) i.commandId: i};
    expect(items[a]!.state, OutboxState.sent);
    expect(items[b]!.state, OutboxState.sent, reason: 'DUPLICATE = already applied = success');
    expect(items[c]!.state, OutboxState.rejected);
    expect(items[c]!.resultCode, 'UNKNOWN_COMMAND');
  });

  test('offline: nothing is lost, the same command id is retried later', () async {
    final id = outbox.enqueue('ReportFault', {'n': 1});
    server.offline = true;
    final status = await sync.syncNow();
    expect(status.online, isFalse);
    expect(outbox.pending().single.commandId, id);
    expect(outbox.pending().single.attempts, 1);

    server.offline = false;
    await sync.syncNow();
    expect(outbox.pendingCount, 0);
    final pushes = server.requests.where((r) => r.url.path == '/sync/v1/push').toList();
    expect(jsonDecode(pushes.last.body).toString(), contains(id));
  });

  test('a revoked device surfaces as DeviceBlockedException', () async {
    server.blockedCode = 'DEVICE_REVOKED';
    await expectLater(sync.syncNow(), throwsA(isA<DeviceBlockedException>()));
  });
}
