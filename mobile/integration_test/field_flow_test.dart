import 'dart:io';
import 'dart:math';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:image/image.dart' as img;
import 'package:integration_test/integration_test.dart';
import 'package:mfop_field/app/app_controller.dart';
import 'package:mfop_field/app/mfop_app.dart';
import 'package:mfop_field/core/config.dart';
import 'package:mfop_field/core/device_info.dart';
import 'package:mfop_field/core/device_trust.dart';
import 'package:mfop_field/core/outbox.dart';
import 'package:mfop_field/core/secure_store.dart';
import 'package:mfop_field/features/work/photo_source.dart';
import 'package:mfop_field/main.dart' show openAppDatabase;
import 'package:mobile_scanner/mobile_scanner.dart';
import 'package:path_provider/path_provider.dart';
import 'package:qr/qr.dart';

import 'support/backoffice.dart';
import 'support/keycloak_login.dart';

/// DEMO 1 steps 6–17 (docs/lean-pilot/11 §M2, §M3) on a real Android system (emulator in CI), against the Docker
/// Compose stack (after `make seed`, for tech1's custody) reached through `adb reverse`:
/// sign in → device registration → approval on the server → app PIN → assigned task in "My tasks" → the printed
/// label's QR decoded by the phone's scanner → asset card → field work (arrive, before photos, diagnosis, start,
/// material from custody, checklist, test photo, after photos, submit) with on-device JPEG processing, hashing,
/// outbox and upload → supervisor approval and seal on the server → revocation → the app deletes only its own data.
///
/// Automation limits (covered by the manual checklist in docs/runbooks/android-field-test.md): Keycloak's login page
/// in the system browser is replaced by the same PKCE flow over HTTP, the QR is decoded from an image file by the
/// on-device ML Kit scanner instead of the live camera, and camera frames come from [_SyntheticCamera] (the in-app
/// camera screen itself is exercised manually on a phone).
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
      photoSource: _SyntheticCamera(),
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

    // On a timeout the screen is captured first (uploaded with the other screenshots), so a stuck step shows its state.
    Future<void> until(Finder finder, {Duration timeout = const Duration(seconds: 40)}) async {
      final end = DateTime.now().add(timeout);
      while (finder.evaluate().isEmpty) {
        if (DateTime.now().isAfter(end)) {
          await binding.takeScreenshot('timeout');
          throw TestFailure('Timed out waiting for $finder');
        }
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
    await until(find.byKey(const Key('waiting-approval')), timeout: const Duration(seconds: 90));
    await shot('03-waiting-for-approval');
    final deviceId = (await store.read(SecureKeys.deviceId))!;
    final pending = (await office.call('admin1', 'GET', '/api/v1/devices?status=PENDING_APPROVAL'))['items'] as List<dynamic>;
    final row = pending.cast<Map<String, dynamic>>().singleWhere((d) => d['id'] == deviceId);
    expect(row['username'], 'tech1');
    expect(row['osVersion'], startsWith('Android'));
    // M4: the phone registered the public half of its Android Keystore key (the private key never leaves the Keystore).
    expect(row['hasKey'], isTrue);
    expect(row['appVersion'], '0.5.0');
    final keystoreKey = await PlatformDeviceTrust().publicKey();

    // Supervisor/admin approves on the server; the phone learns it on its next check.
    await office.call('admin1', 'POST', '/api/v1/devices/$deviceId/approve', {'reason': null});
    await tester.tap(find.byKey(const Key('check-approval')));
    await until(find.byKey(const Key('pin-new')));
    await shot('04-set-app-pin');
    await tester.enterText(find.byKey(const Key('pin-new')), '2468');
    await tester.enterText(find.byKey(const Key('pin-confirm')), '2468');
    await tester.tap(find.byKey(const Key('pin-save')));
    await until(find.byKey(const Key('tab-tasks')));

    // M4: the first sync brought a server-signed offline grant, verified on the phone with the pinned server key.
    final grantEnd = DateTime.now().add(const Duration(seconds: 30));
    while (controller.grants?.current == null) {
      if (DateTime.now().isAfter(grantEnd)) throw TestFailure('no offline grant after the first sync (${controller.message})');
      await tester.pump(const Duration(milliseconds: 500));
    }
    expect(controller.grants!.pinnedKeys, isNotEmpty);
    expect(await controller.grantValid(), isTrue);

    // M5: the seed issued tech1 a driver (D-0004) and fuses that are in transit until tech1 confirms them on the phone.
    await tester.tap(find.byKey(const Key('tab-stock')));
    await until(find.byKey(const Key('issue-serial-D-0004')));
    await shot('04b-material-to-confirm');
    await tester.tap(find.byKey(const Key('issue-serial-D-0004')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('issue-confirm')));
    await until(find.byKey(const Key('issues-empty')));
    final ackEnd = DateTime.now().add(const Duration(seconds: 40));
    while (!controller.custody.any((c) => c.serials.contains('D-0004'))) {
      if (DateTime.now().isAfter(ackEnd)) throw TestFailure('D-0004 did not reach the custody after the acknowledgment');
      await tester.pump(const Duration(milliseconds: 500));
    }

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

    // DEMO 1 steps 10–16 on the phone's screens.
    Future<void> tapKey(String key, {Duration timeout = const Duration(seconds: 40)}) async {
      final f = find.byKey(Key(key));
      await until(f, timeout: timeout);
      await tester.ensureVisible(f);
      await tester.pumpAndSettle();
      await tester.tap(f);
      await tester.pumpAndSettle();
    }

    await tester.tap(find.byKey(const Key('tab-tasks')));
    await tapKey('task-$code');
    await tapKey('step-arrive-action');
    await tester.enterText(find.byKey(const Key('arrive-code')), decoded);
    await tester.enterText(find.byKey(const Key('arrive-reason')), 'اختبار آلي على المحاكي');
    await tapKey('arrive-manual');
    await tapKey('step-before-action');
    await until(find.text('صور قبل العمل (1/2)'));
    await tapKey('step-before-action');
    await until(find.text('صور قبل العمل (2/2)'));
    await shot('08-before-photos');
    await tapKey('step-diagnosis-action');
    await tapKey('diagnosis-LED_MODULE_FAIL');
    await tapKey('diagnosis-save');
    await tapKey('step-start-action');
    await tapKey('step-material-action');
    await tapKey('custody-LED-MOD-50W');
    await tapKey('serial-M-0003');
    await tapKey('material-save');
    // M5 (W9): the failed LED module is declared "with part", photographed, with the serial read on it (a new light: no known serial).
    await tapKey('step-core-action');
    await tester.enterText(find.byKey(const Key('core-serial')), 'OLD-ANDROID-1');
    await tapKey('core-photo-action');
    await until(find.text('الصورة جاهزة'), timeout: const Duration(seconds: 60));
    await shot('08b-failed-part');
    await tapKey('core-save');
    await tapKey('step-checklist-action');
    await tapKey('method-NIGHT');
    // Every item answered with its expected value (the template is reference data pinned to this work order).
    final template = controller.reference.template(controller.cachedTask(workOrder['id'] as String)!.progress!.checklistTemplateId)!;
    for (final item in template.items) {
      await tapKey('item-${item.code}-${item.expected}');
    }
    await shot('09-checklist');
    await tapKey('checklist-save');
    await tapKey('step-test-photo-action');
    await until(find.text('صورة الاختبار (1/1)'));
    await tapKey('step-after-action');
    await until(find.text('صور بعد العمل (1/2)'));
    await tapKey('step-after-action');
    await until(find.text('صور بعد العمل (2/2)'));
    await tapKey('step-submit-action');
    await tester.enterText(find.byKey(const Key('root-cause')), 'احتراق وحدة الليد');
    await tester.pumpAndSettle();
    await tapKey('submit-confirm');
    await until(find.byKey(const Key('submitted-banner')), timeout: const Duration(seconds: 90));
    await shot('10-submitted');
    expect(controller.sync!.evidence.waitingCount, 0); // every photo's bytes reached the server and left the phone
    // Every command was signed by the Keystore key and accepted under the grant (no refusals, nothing held).
    expect(controller.outbox!.all().where((c) => c.state != OutboxState.sent), isEmpty);
    expect(controller.outbox!.all().every((c) => c.grantId != null), isTrue);
    expect(controller.outbox!.all().where((c) => c.payload['workOrderId'] != null).every((c) => c.baseVersion != null), isTrue);

    // Step 17 on the server: every photo VERIFIED, supervisor1 approves → CLOSED with a seal.
    final woPath = '/api/v1/work-orders/${workOrder['id']}';
    final end = DateTime.now().add(const Duration(seconds: 60));
    while ((await office.call('supervisor1', 'GET', '$woPath/review'))['pendingEvidence'] != 0) {
      if (DateTime.now().isAfter(end)) throw TestFailure('evidence not verified in time');
      await Future<void>.delayed(const Duration(seconds: 1));
    }
    final review = await office.call('supervisor1', 'GET', '$woPath/review');
    expect((review['evidence'] as List<dynamic>).length, 5);
    expect((review['materials'] as List<dynamic>).single['serials'], ['M-0003']);
    expect(review['canApprove'], isTrue);
    await office.call('supervisor1', 'POST', '$woPath/approve', {'note': 'android e2e'});
    final closed = await office.call('supervisor1', 'GET', '$woPath/review');
    expect(closed['state'], 'CLOSED');
    expect(closed['sealHash'], matches(RegExp(r'^[0-9a-f]{64}$')));
    await back(find.byKey(const Key('submitted-banner')));
    await tester.tap(find.byKey(const Key('tab-me')));
    await until(find.byKey(const Key('sync-now')));
    await tester.tap(find.byKey(const Key('sync-now')));
    await tester.pump(const Duration(seconds: 2));
    await tester.tap(find.byKey(const Key('tab-tasks')));
    await tester.pumpAndSettle();
    expect(find.byKey(Key('task-$code')), findsNothing); // closed work orders leave My tasks

    // M5: an unused fuse goes back from the phone; the warehouse receives it and the failed module (serial checked).
    await tester.tap(find.byKey(const Key('tab-stock')));
    await tapKey('return-unused');
    await tapKey('return-item-FUSE-10A');
    await tester.enterText(find.byKey(const Key('return-quantity')), '1');
    await tester.pumpAndSettle();
    await tapKey('return-save');
    await until(find.textContaining('RET-'), timeout: const Duration(seconds: 60));
    await shot('10b-return-declared');
    final returns =
        (await office.call('warehouse1', 'GET', '/api/v1/stock/returns?state=DECLARED&technicianId=$technicianId'))['items']
            as List<dynamic>;
    await office.call('warehouse1', 'POST', '/api/v1/stock/returns/${(returns.single as Map<String, dynamic>)['id']}/receive', {
      'confirmedSerials': <String>[],
    });
    final cores =
        (await office.call('warehouse1', 'GET', '/api/v1/stock/core-returns?state=DECLARED_WITH_PART&technicianId=$technicianId'))['items']
            as List<dynamic>;
    final core = cores.cast<Map<String, dynamic>>().singleWhere((c) => c['workOrderId'] == workOrder['id']);
    expect(core['declaredSerial'], 'OLD-ANDROID-1');
    await office.call('warehouse1', 'POST', '/api/v1/stock/core-returns/${core['id']}/receive', {
      'outcome': 'RECEIVED',
      'serial': 'OLD-ANDROID-1',
    });
    final reconciliation = await office.call('warehouse1', 'GET', '/api/v1/stock/reconciliation');
    expect(reconciliation['balanced'], isTrue, reason: '${reconciliation['findings']}');

    // Revocation (server-side only): the phone is refused on its next request and deletes only the app's own data.
    await office.call('admin1', 'POST', '/api/v1/devices/$deviceId/revoke', {'reason': 'نهاية اختبار أندرويد'});
    await tester.tap(find.byKey(const Key('tab-me')));
    await until(find.byKey(const Key('sync-now')));
    await tester.tap(find.byKey(const Key('sync-now')));
    await until(find.byKey(const Key('device-revoked')));
    await shot('11-device-revoked');
    expect(controller.stage, AppStage.deviceRevoked);
    expect(await store.read(SecureKeys.refreshToken), isNull);
    expect(await store.read(SecureKeys.deviceId), isNull);
    expect(await store.read(SecureKeys.pinHash), isNull);
    // The app also deleted its own Keystore device key: a new start creates a different one.
    expect(await PlatformDeviceTrust().publicKey(), isNot(keystoreKey));
  });
}

/// Camera frames for the automated run: a unique 1280×960 JPEG per shot. Everything after the shutter (re-encode,
/// EXIF removal, stamp, SHA-256, AddEvidence, encrypted queue, upload) is the app's real code.
class _SyntheticCamera implements PhotoSource {
  final _random = Random();

  @override
  Future<Uint8List?> capture(BuildContext context, String title) async {
    final frame = img.Image(width: 1280, height: 960);
    img.fill(frame, color: img.ColorRgb8(_random.nextInt(256), _random.nextInt(256), _random.nextInt(256)));
    img.fillRect(
      frame,
      x1: 100,
      y1: 100,
      x2: 100 + _random.nextInt(900),
      y2: 100 + _random.nextInt(700),
      color: img.ColorRgb8(240, 240, 240),
    );
    return img.encodeJpg(frame, quality: 90);
  }
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
