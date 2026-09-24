import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:mfop_field/app/app_controller.dart';
import 'package:mfop_field/app/mfop_app.dart';
import 'package:mfop_field/core/config.dart';
import 'package:mfop_field/core/device_info.dart';
import 'package:mfop_field/core/secure_store.dart';
import 'package:mfop_field/main.dart' show openAppDatabase;
import 'package:mobile_scanner/mobile_scanner.dart';
import 'package:path_provider/path_provider.dart';
import 'package:qr/qr.dart';

import 'support/backoffice.dart';
import 'support/keycloak_login.dart';

/// DEMO 1 steps 6–9 (docs/lean-pilot/11 §M2) on a real Android system (emulator in CI), against the Docker Compose
/// stack reached through `adb reverse`:
/// sign in → device registration → approval on the server → app PIN → assigned task in "My tasks" → the printed
/// label's QR decoded by the phone's scanner → asset card → revocation → the app deletes only its own data.
///
/// Automation limits (covered by the manual checklist in docs/runbooks/android-field-test.md): Keycloak's login page
/// in the system browser is replaced by the same PKCE flow over HTTP, and the QR is decoded from an image file by the
/// on-device ML Kit scanner instead of the live camera.
const technicianId = '0197c000-0000-7000-8000-000000000004'; // tech1 (deploy/seed/dev-users.json)
const password = String.fromEnvironment('MFOP_TEST_PASSWORD');

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('field app: sign in, device approval, PIN, My tasks, QR scan, asset card, revocation', (tester) async {
    expect(password, isNotEmpty, reason: 'pass --dart-define=MFOP_TEST_PASSWORD=<MFOP_DEV_USER_PASSWORD>');
    final config = AppConfig.fromEnvironment();
    final office = Backoffice(
      config.apiBaseUrl,
      KeycloakPkceLogin(issuer: config.issuer, clientId: 'mfop-web', redirectUri: 'http://localhost:5173/', password: password),
    );
    final store = KeystoreSecureStore();
    await store.deleteAll(); // fresh install state
    final controller = AppController(
      config: config,
      store: store,
      auth: TestKeycloakAuth(
        KeycloakPkceLogin(issuer: config.issuer, clientId: config.clientId, redirectUri: config.redirectUri, password: password),
        'tech1',
        store,
      ),
      deviceInfo: PlatformDeviceInfo(),
      openDb: openAppDatabase,
    );

    await binding.convertFlutterSurfaceToImage();
    Future<void> shot(String name) async {
      await tester.pumpAndSettle();
      await binding.takeScreenshot(name);
    }

    // Leaves a pushed screen the way the system back gesture does (independent of the AppBar's back-button widget type).
    Future<void> back(Finder onScreen) async {
      Navigator.of(tester.element(onScreen)).pop();
      await tester.pumpAndSettle();
    }

    Future<void> until(Finder finder, {Duration timeout = const Duration(seconds: 40)}) async {
      final end = DateTime.now().add(timeout);
      while (finder.evaluate().isEmpty) {
        if (DateTime.now().isAfter(end)) throw TestFailure('Timed out waiting for $finder');
        await tester.pump(const Duration(milliseconds: 200));
      }
    }

    await tester.pumpWidget(MfopApp(controller: controller, showCamera: false));
    await controller.start();
    await until(find.byKey(const Key('privacy-accept')));
    await shot('01-privacy-notice');

    await tester.tap(find.byKey(const Key('privacy-accept')));
    await until(find.byKey(const Key('sign-in')));
    await shot('02-sign-in');

    // Step: technician signs in; the app registers the phone and waits for approval.
    await tester.tap(find.byKey(const Key('sign-in')));
    await until(find.byKey(const Key('waiting-approval')));
    await shot('03-waiting-for-approval');
    final deviceId = (await store.read(SecureKeys.deviceId))!;
    final pending = (await office.call('admin1', 'GET', '/api/v1/devices?status=PENDING_APPROVAL'))['items'] as List<dynamic>;
    final row = pending.cast<Map<String, dynamic>>().singleWhere((d) => d['id'] == deviceId);
    expect(row['username'], 'tech1');
    expect(row['osVersion'], startsWith('Android'));

    // Supervisor/admin approves on the server; the phone learns it on its next check.
    await office.call('admin1', 'POST', '/api/v1/devices/$deviceId/approve', {'reason': null});
    await tester.tap(find.byKey(const Key('check-approval')));
    await until(find.byKey(const Key('pin-new')));
    await shot('04-set-app-pin');
    await tester.enterText(find.byKey(const Key('pin-new')), '2468');
    await tester.enterText(find.byKey(const Key('pin-confirm')), '2468');
    await tester.tap(find.byKey(const Key('pin-save')));
    await until(find.byKey(const Key('tab-tasks')));

    // DEMO 1 steps 8–9 on the web side: a synthetic light, a fault, a work order assigned to tech1.
    final address = 'شارع أندرويد ${DateTime.now().millisecondsSinceEpoch % 100000}';
    final asset = await office.activeStreetLight(address);
    final assetCode = asset['assetCode'] as String;
    final fault = await office.call('supervisor1', 'POST', '/api/v1/faults', {
      'assetId': asset['id'],
      'category': 'LIGHT_OFF',
      'powerAtInspection': 'CONFIRMED_AVAILABLE',
      'powerInfoSource': 'OBSERVED_ON_SITE',
      'description': 'لا تضيء ليلًا',
    });
    final workOrder = await office.call('supervisor1', 'POST', '/api/v1/work-orders', {
      'faultId': fault['faultId'],
      'priority': 'P1',
      'note': 'تفقد وإصلاح',
    });
    await office.call('supervisor1', 'POST', '/api/v1/work-orders/${workOrder['id']}/assign', {'assigneeId': technicianId});
    final code = (await office.call('supervisor1', 'GET', '/api/v1/work-orders/${workOrder['id']}'))['code'] as String;

    // The task appears on the technician's phone.
    await tester.tap(find.byKey(const Key('tab-me')));
    await until(find.byKey(const Key('sync-now')));
    await tester.tap(find.byKey(const Key('sync-now')));
    await tester.pump(const Duration(seconds: 2));
    await tester.tap(find.byKey(const Key('tab-tasks')));
    await until(find.byKey(Key('task-$code')));
    await shot('05-my-tasks');
    await tester.tap(find.byKey(Key('task-$code')));
    await until(find.byKey(const Key('task-state')));
    expect(find.text(assetCode), findsWidgets);
    expect(find.text('معطّل'), findsOneWidget);
    await shot('06-task-detail');
    await back(find.byKey(const Key('task-state')));

    // Step 6: the printed label's QR, decoded on the phone by the same scanner library as the Scan tab (ML Kit).
    final qrPayload = (asset['tag'] as Map<String, dynamic>)['qrPayload'] as String;
    final decoded = await _decodeOnDevice(await _renderQrPng(qrPayload));
    expect(decoded, qrPayload);

    // Step 7: the asset record, resolved online, shows my open task on this light.
    await tester.tap(find.byKey(const Key('tab-scan')));
    await until(find.byKey(const Key('manual-code')));
    await tester.enterText(find.byKey(const Key('manual-code')), decoded!);
    await tester.tap(find.byKey(const Key('manual-resolve')));
    await until(find.byKey(Key('card-task-$code')));
    expect(find.text(assetCode), findsWidgets);
    await shot('07-asset-card-from-qr');
    await back(find.byKey(const Key('open-faults')));

    // Revocation (server-side only): the phone is refused on its next request and deletes only the app's own data.
    await office.call('admin1', 'POST', '/api/v1/devices/$deviceId/revoke', {'reason': 'نهاية اختبار أندرويد'});
    await tester.tap(find.byKey(const Key('tab-me')));
    await until(find.byKey(const Key('sync-now')));
    await tester.tap(find.byKey(const Key('sync-now')));
    await until(find.byKey(const Key('device-revoked')));
    await shot('08-device-revoked');
    expect(controller.stage, AppStage.deviceRevoked);
    expect(await store.read(SecureKeys.refreshToken), isNull);
    expect(await store.read(SecureKeys.deviceId), isNull);
    expect(await store.read(SecureKeys.pinHash), isNull);
  });
}

/// Renders the label QR (same payload as the printed A6 label) to a PNG file inside the app's cache directory.
Future<String> _renderQrPng(String payload) async {
  final qr = QrImage(QrCode(payload: QrPayload.fromString(payload), errorCorrectLevel: QrErrorCorrectLevel.medium));
  const scale = 12, quiet = 4;
  final size = (qr.moduleCount + 2 * quiet) * scale;
  final recorder = ui.PictureRecorder();
  final canvas = Canvas(recorder)..drawRect(Rect.fromLTWH(0, 0, size.toDouble(), size.toDouble()), Paint()..color = Colors.white);
  final black = Paint()..color = Colors.black;
  for (var y = 0; y < qr.moduleCount; y++) {
    for (var x = 0; x < qr.moduleCount; x++) {
      if (qr.isDark(y, x)) {
        canvas.drawRect(
          Rect.fromLTWH(((x + quiet) * scale).toDouble(), ((y + quiet) * scale).toDouble(), scale.toDouble(), scale.toDouble()),
          black,
        );
      }
    }
  }

  final image = await recorder.endRecording().toImage(size, size);
  final png = await image.toByteData(format: ui.ImageByteFormat.png);
  final file = File('${(await getTemporaryDirectory()).path}/label-qr.png');
  await file.writeAsBytes(png!.buffer.asUint8List());
  return file.path;
}

Future<String?> _decodeOnDevice(String path) async {
  final scanner = MobileScannerController(formats: const [BarcodeFormat.qrCode]);
  try {
    final capture = await scanner.analyzeImage(path);
    return capture?.barcodes.firstOrNull?.rawValue;
  } finally {
    await scanner.dispose();
  }
}
