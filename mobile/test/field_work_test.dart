import 'dart:convert';

import 'package:crypto/crypto.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:image/image.dart' as img;
import 'package:mfop_field/core/evidence_queue.dart';
import 'package:mfop_field/core/photo_processing.dart';
import 'package:mfop_field/core/secure_store.dart';
import 'package:mfop_field/features/work/work_screens.dart';

import 'support.dart';

Future<Harness> readyTechnician({Map<String, dynamic>? task}) async {
  final h = Harness();
  await h.store.write(SecureKeys.privacyAccepted, 'yes');
  await h.auth.signIn();
  h.server.deviceStatus = 'ACTIVE';
  h.server.tasks = [task ?? {...taskJson(), 'progress': progressJson()}];
  await h.controller.start();
  await h.controller.setPin('2468');
  return h;
}

List<Map<String, dynamic>> pushedCommands(Harness h) => [
  for (final r in h.server.requests.where((r) => r.url.path == '/sync/v1/push'))
    for (final c in (jsonDecode(r.body) as Map<String, dynamic>)['commands'] as List<dynamic>) c as Map<String, dynamic>,
];

void main() {
  group('photo processing', () {
    test('re-encodes to at most 2048 px, strips EXIF, stamps the image and hashes the final bytes', () {
      final big = img.Image(width: 3000, height: 2000);
      img.fill(big, color: img.ColorRgb8(10, 120, 60));
      big.exif.imageIfd['Make'] = 'SecretPhoneMaker';
      big.exif.gpsIfd['GPSLatitude'] = 33.5;
      final raw = img.encodeJpg(big);
      expect(img.decodeJpg(raw)!.exif.isEmpty, isFalse);

      final photo = processPhoto(PhotoRequest(raw, 'WO-2026-000001 BEFORE 2026-09-25 10:00:00Z'));
      expect(photo.width, 2048);
      expect(photo.height, 1365);
      final decoded = img.decodeJpg(photo.bytes)!;
      expect(decoded.exif.isEmpty, isTrue);
      expect(utf8.decode(photo.bytes, allowMalformed: true), isNot(contains('SecretPhoneMaker')));
      expect(photo.sha256, sha256.convert(photo.bytes).toString());
    });

    test('the same frame with a different stamp is a different photo (hash)', () {
      final raw = cameraJpeg(width: 800, height: 600);
      final a = processPhoto(PhotoRequest(raw, 'WO-1 BEFORE 2026-09-25 10:00:00Z'));
      final b = processPhoto(PhotoRequest(raw, 'WO-1 BEFORE 2026-09-25 10:00:01Z'));
      expect(a.sha256, isNot(b.sha256));
    });

    test('stamp is ASCII and in UTC', () {
      expect(photoStamp('WO-2026-000004', 'AFTER', DateTime.utc(2026, 9, 25, 7, 5, 9)), 'WO-2026-000004 AFTER 2026-09-25 07:05:09Z');
    });

    test('bytes that are not an image are refused', () {
      expect(() => processPhoto(PhotoRequest(utf8.encode('not a jpeg'), 'x')), throwsFormatException);
    });
  });

  group('technician flow through the outbox', () {
    test('every step is a command sent in order; photos are announced first, uploaded after APPLIED, then deleted locally', () async {
      final h = await readyTechnician();
      final c = h.controller;
      final task = c.tasks.single;
      expect(c.reference.repairCodesFor('SOLAR').map((d) => d.code), ['LED_MODULE_FAIL', 'BATTERY_FAIL']); // NO_WORK and grid-only codes hidden
      expect(c.custody.map((x) => x.sku), ['LED-MOD-50W', 'FUSE-10A']);

      await c.arrive(task, scanned: 'http://web.test/t/7K3M92QX.4F');
      for (final kind in ['BEFORE', 'BEFORE']) {
        await c.addPhoto(task, kind, processPhoto(PhotoRequest(cameraJpeg(seed: kind.length + c.outbox!.all().length), 'WO $kind')), DateTime.utc(2026, 9, 25, 10));
      }
      await c.recordDiagnosis(task, 'LED_MODULE_FAIL', ' وحدة الليد ');
      await c.startWork(task);
      await c.consume(task, c.custody.first, 1, ['M-0003']);
      await c.recordChecklist(task, 'tpl-solar', 'NIGHT', {'LAMP_ON': 'YES', 'EXPOSED_WIRES': 'NO'}, null);
      for (final kind in ['TEST', 'AFTER', 'AFTER']) {
        await c.addPhoto(task, kind, processPhoto(PhotoRequest(cameraJpeg(seed: 10 + c.outbox!.all().length), 'WO $kind')), DateTime.utc(2026, 9, 25, 11));
      }

      final view = c.workView(task);
      expect(view.state, 'IN_PROGRESS');
      expect(view.beforePhotos, 2);
      expect(view.afterPhotos, 2);
      expect(view.testPhotos, 1);
      expect(view.readyToSubmit, isTrue);

      await c.submit(task, rootCause: 'احتراق وحدة الليد', resubmit: false);
      await c.syncNow();

      final sent = pushedCommands(h);
      expect(sent.map((x) => x['type']), [
        'ArriveOnSite', 'AddEvidence', 'AddEvidence', 'RecordDiagnosis', 'StartWork', 'ConsumeMaterial', 'RecordChecklist',
        'AddEvidence', 'AddEvidence', 'AddEvidence', 'SubmitWorkOrder',
      ]);
      expect(sent.every((x) => x['payload']['workOrderId'] == task.id), isTrue);
      expect(sent[3]['payload']['note'], 'وحدة الليد');
      expect(sent[5]['payload']['serials'], ['M-0003']);
      final evidence = sent.where((x) => x['type'] == 'AddEvidence').map((x) => x['payload'] as Map<String, dynamic>).toList();
      expect(h.server.uploads.keys.toSet(), evidence.map((e) => e['evidenceId']).toSet());
      for (final e in evidence) {
        expect(sha256.convert(h.server.uploads[e['evidenceId']]!).toString(), e['sha256']);
      }

      final uploadAt = h.server.requests.indexWhere((r) => r.method == 'PUT');
      final pushAt = h.server.requests.indexWhere((r) => r.url.path == '/sync/v1/push');
      expect(uploadAt, greaterThan(pushAt)); // bytes only after the server accepted the announcement
      final queue = c.sync!.evidence;
      expect(queue.waitingCount, 0);
      for (final p in queue.forWorkOrder(task.id)) {
        expect(p.state, EvidenceUploadState.uploaded);
        expect(queue.bytesOf(p.evidenceId), isNull); // the phone's copy is gone
      }
    });

    test('a refused announcement is shown on the task and its photo is never uploaded', () async {
      final h = await readyTechnician(task: {...taskJson(), 'state': 'ON_SITE', 'progress': progressJson(arrivals: 1)});
      h.server.pushResults = (commands) => [
        for (final cmd in commands)
          {'commandId': (cmd as Map)['commandId'], 'result': 'REJECTED', 'code': 'EVIDENCE_KIND_NOT_NOW', 'message': 'no', 'entityId': null},
      ];
      final c = h.controller;
      final task = c.tasks.single;
      await c.addPhoto(task, 'AFTER', processPhoto(PhotoRequest(cameraJpeg(), 'x')), DateTime.utc(2026, 9, 25));
      await c.syncNow();

      expect(h.server.uploads, isEmpty);
      final view = c.workView(task);
      expect(view.rejections.single.code, 'EVIDENCE_KIND_NOT_NOW');
      expect(view.photosFailed, 1);
      expect(c.sync!.evidence.forWorkOrder(task.id).single.state, EvidenceUploadState.failed);
    });

    test('offline: commands and photos wait on the phone and go out, in order, when the network is back', () async {
      final h = await readyTechnician();
      final c = h.controller;
      final task = c.tasks.single;
      h.server.offline = true;
      await c.arrive(task, manualCode: '7k3m92qx.4f', manualReason: 'الملصق ممزق');
      await c.addPhoto(task, 'BEFORE', processPhoto(PhotoRequest(cameraJpeg(seed: 3), 'x')), DateTime.utc(2026, 9, 25));
      await c.syncNow();
      expect(c.syncStatus!.online, isFalse);
      expect(c.syncStatus!.pending, 2);
      expect(c.syncStatus!.photosWaiting, 1);
      expect(c.workView(task).state, 'ON_SITE'); // shown locally before the server knows

      h.server.offline = false;
      await c.syncNow();
      final sent = pushedCommands(h);
      expect(sent.map((x) => x['type']), ['ArriveOnSite', 'AddEvidence']);
      expect(sent.first['payload']['manualCode'], '7K3M92QX.4F');
      expect(sent.first['payload']['manualReason'], 'الملصق ممزق');
      expect(h.server.uploads, hasLength(1));
      expect(c.syncStatus!.photosWaiting, 0);
    });

    test('a typed code is sent as the tag token, even if the label URL was pasted', () async {
      final h = await readyTechnician();
      final c = h.controller;
      await c.arrive(c.tasks.single, manualCode: 'http://localhost:5173/t/7K3M92QX.4F', manualReason: 'الملصق ممزق');
      await c.syncNow();
      expect(pushedCommands(h).single['payload']['manualCode'], '7K3M92QX.4F');
    });

    test('a returned task offers additions and Resubmit', () async {
      final h = await readyTechnician(task: {
        ...taskJson(),
        'state': 'RETURNED',
        'progress': {
          ...progressJson(arrivals: 1, diagnosis: 'LED_MODULE_FAIL', before: 2, after: 2, test: 1, materials: 1, checklist: true),
          'lastReturnReasons': ['UNCLEAR_EVIDENCE'],
          'lastReturnNote': 'صورة بعد العمل غير واضحة',
        },
      });
      final c = h.controller;
      final task = c.tasks.single;
      final view = c.workView(task);
      expect(view.isReturned, isTrue);
      expect(view.canRecordWork, isTrue);
      expect(view.readyToSubmit, isTrue);
      await c.addPhoto(task, 'AFTER', processPhoto(PhotoRequest(cameraJpeg(seed: 9), 'x')), DateTime.utc(2026, 9, 25));
      await c.submit(task, rootCause: 'احتراق', resubmit: true);
      await c.syncNow();
      expect(pushedCommands(h).map((x) => x['type']), ['AddEvidence', 'Resubmit']);
    });
  });
}
