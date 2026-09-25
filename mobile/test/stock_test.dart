import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mfop_field/app/mfop_app.dart';
import 'package:mfop_field/core/field_work.dart';
import 'package:mfop_field/core/models.dart';
import 'package:mfop_field/core/outbox.dart';
import 'package:mfop_field/core/secure_store.dart';

import 'support.dart';

OutboxItem command(int seq, String type, Map<String, dynamic> payload, [OutboxState state = OutboxState.pending]) =>
    OutboxItem('c$seq', seq, type, payload, DateTime.utc(2026, 9, 25), state, 0, null, null);

/// M5 on the phone (docs/lean-pilot/04 §4.9–4.11).
void main() {
  final items = itemsJson.map(StockItemRef.fromJson).toList();

  test('a failed part to declare is known offline from the queued consumption, and disappears once declared', () {
    const empty = TechnicianStock();
    final consume = command(1, 'ConsumeMaterial', {'workOrderId': 'wo-1', 'movementId': 'mv-1', 'itemId': 'item-led', 'quantity': 1, 'serials': ['M-0003']});
    final fuse = command(2, 'ConsumeMaterial', {'workOrderId': 'wo-1', 'movementId': 'mv-2', 'itemId': 'item-fuse', 'quantity': 2});
    final pending = pendingCoreObligations('wo-1', empty, items, [consume, fuse]);
    expect(pending, hasLength(1)); // the fuse is not a core-return item
    expect(pending.single.movementId, 'mv-1');
    expect(pending.single.installedSerial, 'M-0003');

    final declared = command(3, 'DeclareCoreReturn', {'workOrderId': 'wo-1', 'movementId': 'mv-1', 'installedSerial': 'M-0003', 'outcome': 'WITH_PART'});
    expect(pendingCoreObligations('wo-1', empty, items, [consume, fuse, declared]), isEmpty);
    // A refused declaration does not count.
    final refused = command(3, 'DeclareCoreReturn', {'workOrderId': 'wo-1', 'movementId': 'mv-1', 'outcome': 'WITH_PART'}, OutboxState.rejected);
    expect(pendingCoreObligations('wo-1', empty, items, [consume, refused]), hasLength(1));

    // After sync the server's OPEN record replaces the local guess (not counted twice); another work order is not mixed in.
    final server = TechnicianStock(coreReturns: [
      CoreReturnView(id: 'core-1', code: 'CORE-1', state: 'OPEN', consumeMovementId: 'mv-1', workOrderId: 'wo-1', workOrderCode: 'WO-1',
          sku: 'LED-MOD-50W', nameAr: 'x', nameEn: 'x', serialTracked: true, installedSerial: 'M-0003', expectedSerial: 'M-0002'),
      CoreReturnView(id: 'core-2', code: 'CORE-2', state: 'OPEN', consumeMovementId: 'mv-9', workOrderId: 'wo-2', workOrderCode: 'WO-2',
          sku: 'LED-MOD-50W', nameAr: 'x', nameEn: 'x', serialTracked: true),
    ]);
    final sent = command(1, 'ConsumeMaterial', consume.payload, OutboxState.sent);
    final afterSync = pendingCoreObligations('wo-1', server, items, [sent]);
    expect(afterSync.single.coreReturnId, 'core-1');
    expect(afterSync.single.expectedSerial, 'M-0002');
  });

  test('issues already answered on the phone are not asked again', () {
    final stock = TechnicianStock(issues: [StockIssueView.fromJson(issueJson()), StockIssueView.fromJson(issueJson(id: 'iss-2', code: 'ISS-8'))]);
    expect(issuesToAnswer(stock, [command(1, 'AcknowledgeStockIssue', {'issueId': 'iss-1'})]).map((i) => i.id), ['iss-2']);
    expect(issuesToAnswer(stock, [command(1, 'AcknowledgeStockIssue', {'issueId': 'iss-1'}, OutboxState.rejected)]), hasLength(2));
  });

  testWidgets('Materials tab: confirm an issue only with every serial ticked, report a problem, return unused material', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.75;
    addTearDown(tester.view.reset);

    final h = Harness();
    await h.store.write(SecureKeys.privacyAccepted, 'yes');
    await h.auth.signIn();
    h.server.deviceStatus = 'ACTIVE';
    h.server.simulateWorkOrders = true;
    h.server.items = itemsJson;
    h.server.stock = {
      'issues': [issueJson(serials: ['D-0004', 'D-0005']), issueJson(id: 'iss-2', code: 'ISS-000008', serials: ['D-0006'])],
      'returns': [],
      'coreReturns': [],
    };
    await tester.runAsync(() async {
      await h.controller.start();
      await h.controller.setPin('2468');
    });
    await tester.pumpWidget(MfopApp(controller: h.controller, showCamera: false));
    await tester.pumpAndSettle();

    Future<void> tap(Finder f) async {
      await tester.ensureVisible(f);
      await tester.pumpAndSettle();
      await tester.runAsync(() async {
        await tester.tap(f);
        await Future<void>.delayed(const Duration(milliseconds: 50));
      });
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 150)));
      await tester.pumpAndSettle();
    }

    await tap(find.byKey(const Key('tab-stock')));
    final first = find.byKey(const Key('issue-ISS-000007'));
    final confirm = find.descendant(of: first, matching: find.byKey(const Key('issue-confirm')));
    expect(tester.widget<FilledButton>(confirm).onPressed, isNull); // two-party handover: tick what arrived
    await tap(find.byKey(const Key('issue-serial-D-0004')));
    expect(tester.widget<FilledButton>(confirm).onPressed, isNull);
    await tap(find.byKey(const Key('issue-serial-D-0005')));
    await tap(confirm);
    expect(find.byKey(const Key('issue-ISS-000007')), findsNothing);

    await tap(find.descendant(of: find.byKey(const Key('issue-ISS-000008')), matching: find.byKey(const Key('issue-dispute'))));
    await tester.enterText(find.byKey(const Key('dispute-reason')), 'لم يصل الدرايفر');
    await tap(find.byKey(const Key('dispute-send')));
    expect(find.byKey(const Key('issues-empty')), findsOneWidget);

    await tap(find.byKey(const Key('return-unused')));
    await tap(find.byKey(const Key('return-item-FUSE-10A')));
    await tester.enterText(find.byKey(const Key('return-quantity')), '2');
    await tester.pump();
    await tap(find.byKey(const Key('return-save')));

    final pushed = [
      for (final r in h.server.requests.where((r) => r.url.path == '/sync/v1/push'))
        for (final c in (jsonDecode(r.body) as Map<String, dynamic>)['commands'] as List<dynamic>) c as Map<String, dynamic>,
    ];
    expect(pushed.map((c) => c['type']), ['AcknowledgeStockIssue', 'DisputeStockIssue', 'DeclareStockReturn']);
    expect(pushed[0]['payload'], {'issueId': 'iss-1', 'confirmedSerials': ['D-0004', 'D-0005']});
    expect(pushed[0]['baseVersion'], isNull);
    expect(pushed[0]['grantId'], isNotNull); // made under the offline grant, like every write
    expect(pushed[1]['payload']['reason'], 'لم يصل الدرايفر');
    expect(pushed[2]['payload']['lines'], [
      {'itemId': 'item-fuse', 'quantity': 2.0, 'serials': null},
    ]);
    expect(find.byKey(const Key('return-RET-000001')), findsOneWidget);
  });
}
