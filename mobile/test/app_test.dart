import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mfop_field/app/mfop_app.dart';
import 'package:mfop_field/core/secure_store.dart';

import 'support.dart';

Future<Harness> _signedOut(WidgetTester tester) async {
  final h = Harness();
  await h.store.write(SecureKeys.privacyAccepted, 'yes');
  await tester.runAsync(h.controller.start);
  await tester.pumpWidget(MfopApp(controller: h.controller, showCamera: false));
  await tester.pumpAndSettle();
  return h;
}

void main() {
  testWidgets('starts in Arabic with right-to-left layout', (tester) async {
    await _signedOut(tester);
    expect(find.text('تسجيل الدخول'), findsWidgets);
    final direction = Directionality.of(tester.element(find.byKey(const Key('environment'))));
    expect(direction, TextDirection.rtl);
    expect(find.text('البيئة: TEST'), findsOneWidget);
  });

  testWidgets('can switch to English (left-to-right)', (tester) async {
    await _signedOut(tester);
    await tester.tap(find.byKey(const Key('toggle-language')));
    await tester.pumpAndSettle();
    expect(find.text('Sign in'), findsWidgets);
    expect(Directionality.of(tester.element(find.byKey(const Key('environment')))), TextDirection.ltr);
  });

  testWidgets('primary button meets the 56 dp field touch-target minimum', (tester) async {
    await _signedOut(tester);
    expect(tester.getSize(find.byKey(const Key('sign-in'))).height, greaterThanOrEqualTo(56));
  });
}
