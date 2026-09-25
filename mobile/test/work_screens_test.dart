import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mfop_field/app/mfop_app.dart';
import 'package:mfop_field/core/secure_store.dart';

import 'support.dart';

/// DEMO 1 steps 10–16 on the phone's screens: every tap below is what a technician does.
void main() {
  testWidgets('technician completes a work order on the phone: arrive, photos, diagnosis, start, material, checklist, submit', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.75;
    addTearDown(tester.view.reset);

    final h = Harness();
    await h.store.write(SecureKeys.privacyAccepted, 'yes');
    await h.auth.signIn();
    h.server.deviceStatus = 'ACTIVE';
    h.server.simulateWorkOrders = true;
    h.server.tasks = [{...taskJson(code: 'WO-2026-000009'), 'progress': progressJson()}];
    await tester.runAsync(() async {
      await h.controller.start();
      await h.controller.setPin('2468');
    });
    await tester.pumpWidget(MfopApp(controller: h.controller, showCamera: false));
    await tester.pumpAndSettle();

    Future<void> settle() async {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 150)));
      await tester.pumpAndSettle();
    }

    Future<void> tap(String key) async {
      final f = find.byKey(Key(key));
      await tester.ensureVisible(f);
      await tester.pumpAndSettle();
      await tester.runAsync(() async {
        await tester.tap(f);
        await Future<void>.delayed(const Duration(milliseconds: 50));
      });
      await settle();
    }

    Future<void> photo(String step) async {
      final f = find.byKey(Key('$step-action'));
      await tester.ensureVisible(f);
      await tester.pumpAndSettle();
      await tester.runAsync(() async {
        await tester.tap(f);
        await Future<void>.delayed(const Duration(milliseconds: 50));
      });
      // On-device processing runs in a background isolate in real time and a progress bar animates meanwhile, so
      // pumpAndSettle only once it is gone (a slow machine may need several seconds).
      for (var i = 0; i < 200 && (i == 0 || find.byType(LinearProgressIndicator).evaluate().isNotEmpty); i++) {
        await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 100)));
        await tester.pump();
      }

      expect(find.byType(LinearProgressIndicator), findsNothing, reason: 'photo processing did not finish');
      await settle();
    }

    await tap('task-WO-2026-000009');
    expect(find.byKey(const Key('step-before-action')), findsNothing); // nothing before arriving

    // Arrive with a typed code (damaged tag) and a reason.
    await tap('step-arrive-action');
    await tester.enterText(find.byKey(const Key('arrive-code')), '7K3M92QX.4F');
    await tap('arrive-manual');
    expect(find.byKey(const Key('arrive-error')), findsOneWidget); // reason required
    await tester.enterText(find.byKey(const Key('arrive-reason')), 'الملصق ممزق');
    await tap('arrive-manual');

    await photo('step-before');
    await photo('step-before');
    expect(find.text('صور قبل العمل (2/2)'), findsOneWidget);

    await tap('step-diagnosis-action');
    expect(find.byKey(const Key('diagnosis-FUSE')), findsNothing); // grid-only code hidden on a solar light
    await tap('diagnosis-LED_MODULE_FAIL');
    await tap('diagnosis-save');

    await tap('step-start-action');
    await tap('step-material-action');
    await tap('custody-LED-MOD-50W');
    await tap('serial-M-0003');
    await tap('material-save');

    await tap('step-checklist-action');
    await tap('method-NIGHT');
    await tap('item-LAMP_ON-YES');
    await tap('item-EXPOSED_WIRES-NO');
    await tap('checklist-save');

    await photo('step-test-photo');
    await photo('step-after');
    await photo('step-after');

    await tap('step-submit-action');
    await tester.enterText(find.byKey(const Key('root-cause')), 'احتراق وحدة الليد');
    await tester.pump();
    await tap('submit-confirm');
    expect(find.byKey(const Key('submitted-banner')), findsOneWidget);

    final types = [
      for (final r in h.server.requests.where((r) => r.url.path == '/sync/v1/push'))
        for (final c in (jsonDecode(r.body) as Map<String, dynamic>)['commands'] as List<dynamic>) (c as Map<String, dynamic>)['type'],
    ];
    expect(types, [
      'ArriveOnSite', 'AddEvidence', 'AddEvidence', 'RecordDiagnosis', 'StartWork', 'ConsumeMaterial', 'RecordChecklist',
      'AddEvidence', 'AddEvidence', 'AddEvidence', 'SubmitWorkOrder',
    ]);
    expect(h.server.uploads, hasLength(5));
  });
}
