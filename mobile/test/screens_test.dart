import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mfop_field/app/app_controller.dart';
import 'package:mfop_field/app/mfop_app.dart';
import 'package:mfop_field/core/secure_store.dart';

import 'support.dart';

Future<Harness> _ready(WidgetTester tester) async {
  final h = Harness();
  await h.store.write(SecureKeys.privacyAccepted, 'yes');
  await h.auth.signIn();
  h.server.deviceStatus = 'ACTIVE';
  h.server.tasks = [taskJson(code: 'WO-2026-000007', priority: 'P1')];
  await tester.runAsync(() async {
    await h.controller.start();
    await h.controller.setPin('2468');
  });
  await tester.pumpWidget(MfopApp(controller: h.controller, showCamera: false));
  await tester.pumpAndSettle();
  return h;
}

void main() {
  testWidgets('My tasks lists the assigned work order and opens it with the asset card', (tester) async {
    final h = await _ready(tester);
    expect(h.controller.stage, AppStage.ready);
    expect(find.byKey(const Key('sync-strip')), findsOneWidget);
    expect(find.textContaining('SL-000042'), findsOneWidget);
    expect(find.textContaining('لا تضيء'), findsOneWidget);

    await tester.runAsync(() async {
      await tester.tap(find.byKey(const Key('task-WO-2026-000007')));
      await Future<void>.delayed(const Duration(milliseconds: 50));
    });
    await tester.pumpAndSettle();
    expect(find.text('أمر العمل WO-2026-000007'), findsOneWidget);
    expect(find.byKey(const Key('asset-status')), findsOneWidget);
    expect(find.text('معطّل'), findsOneWidget);
    expect(find.textContaining('شمسية'), findsOneWidget);
  });

  testWidgets('Scan tab: a typed code shows the asset card with my task; a bad code shows an error', (tester) async {
    await _ready(tester);
    await tester.tap(find.byKey(const Key('tab-scan')));
    await tester.pumpAndSettle();

    await tester.enterText(find.byKey(const Key('manual-code')), 'abc');
    await tester.tap(find.byKey(const Key('manual-resolve')));
    await tester.pumpAndSettle();
    expect(find.text('رمز غير صالح.'), findsOneWidget);

    await tester.enterText(find.byKey(const Key('manual-code')), '7K3M92QX.4F');
    await tester.runAsync(() async {
      await tester.tap(find.byKey(const Key('manual-resolve')));
      await Future<void>.delayed(const Duration(milliseconds: 50));
    });
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('asset-code')), findsOneWidget);
    expect(find.text('أعطال مفتوحة: 1'), findsOneWidget);
    expect(find.byKey(const Key('card-task-WO-2026-000007')), findsOneWidget);
  });

  testWidgets('Me tab shows the user, the empty outbox and a 56 dp sync button', (tester) async {
    await _ready(tester);
    await tester.tap(find.byKey(const Key('tab-me')));
    await tester.pumpAndSettle();
    expect(find.text('فني تجريبي'), findsOneWidget);
    expect(find.text('لا يوجد ما ينتظر الإرسال.'), findsOneWidget);
    expect(tester.getSize(find.byKey(const Key('sync-now'))).height, greaterThanOrEqualTo(56));
  });
}
