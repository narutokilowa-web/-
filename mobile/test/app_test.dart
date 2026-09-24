import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mfop_field/app/mfop_app.dart';

void main() {
  testWidgets('starts in Arabic with right-to-left layout', (tester) async {
    await tester.pumpWidget(const MfopApp());
    await tester.pumpAndSettle();

    expect(find.text('تطبيق العمليات الميدانية'), findsOneWidget);
    final direction = Directionality.of(tester.element(find.byKey(const Key('environment'))));
    expect(direction, TextDirection.rtl);
    expect(find.text('البيئة: DEV'), findsOneWidget);
  });

  testWidgets('can switch to English (left-to-right)', (tester) async {
    await tester.pumpWidget(const MfopApp());
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('toggle-language')));
    await tester.pumpAndSettle();

    expect(find.text('Field Operations App'), findsOneWidget);
    final direction = Directionality.of(tester.element(find.byKey(const Key('environment'))));
    expect(direction, TextDirection.ltr);
  });

  testWidgets('primary button meets the 56 dp field touch-target minimum', (tester) async {
    await tester.pumpWidget(const MfopApp());
    await tester.pumpAndSettle();

    final size = tester.getSize(find.byKey(const Key('toggle-language')));
    expect(size.height, greaterThanOrEqualTo(56));
  });
}
