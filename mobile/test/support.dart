import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/widgets.dart';
import 'package:image/image.dart' as img;

import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:mfop_field/app/app_controller.dart';
import 'package:mfop_field/core/api_client.dart';
import 'package:mfop_field/core/auth.dart';
import 'package:mfop_field/core/config.dart';
import 'package:mfop_field/core/device_info.dart';
import 'package:mfop_field/core/local_db.dart';
import 'package:mfop_field/core/pin.dart';
import 'package:mfop_field/core/secure_store.dart';
import 'package:mfop_field/features/work/photo_source.dart';

const testConfig = AppConfig(
  apiBaseUrl: 'http://api.test',
  issuer: 'http://kc.test/realms/mfop',
  clientId: 'mfop-mobile',
  redirectUri: 'mfop://oauth-callback',
  environment: 'TEST',
  appVersion: '0.2.0',
  allowInsecureConnections: true,
  appLockMinutes: 5,
);

class FakeAuth implements AuthService {
  FakeAuth(this.store);

  final SecureStore store;
  int signIns = 0;

  @override
  Future<bool> hasSession() async => await store.read(SecureKeys.refreshToken) != null;

  @override
  Future<void> signIn() async {
    signIns++;
    await store.write(SecureKeys.refreshToken, 'refresh');
  }

  @override
  Future<String?> accessToken() async => await hasSession() ? 'access' : null;

  @override
  Future<void> signOut() => store.delete(SecureKeys.refreshToken);
}

class FakeDeviceInfo implements DeviceInfoSource {
  @override
  Future<DeviceDescription> describe() async => const DeviceDescription('Test Phone', 'Android 14');
}

Map<String, dynamic> assetJson({String status = 'FAULTY'}) => {
  'id': 'a-1',
  'assetCode': 'SL-000042',
  'lifecycleState': 'ACTIVE',
  'operationalStatus': status,
  'powerSource': 'SOLAR',
  'responsibleParty': 'MUNICIPALITY',
  'responsibleEntityName': null,
  'addressText': 'شارع الاختبار',
  'latitude': 33.5,
  'longitude': 36.3,
};

Map<String, dynamic> taskJson({String code = 'WO-2026-000001', String priority = 'P2'}) => {
  'id': 'wo-$code',
  'code': code,
  'state': 'ASSIGNED',
  'priority': priority,
  'slaDueAt': null,
  'note': 'تفقد وإصلاح',
  'faultCategory': 'LIGHT_OFF',
  'faultDescription': 'لا تضيء ليلًا',
  'faultPowerAtInspection': 'CONFIRMED_AVAILABLE',
  'assignedAt': '2026-09-24T10:00:00Z',
  'asset': assetJson(),
};

/// A scripted API server. Tests change [deviceStatus], [tasks] or [blockedCode] to simulate the supervisor's actions.
Map<String, dynamic> progressJson({int arrivals = 0, String? diagnosis, int before = 0, int after = 0, int test = 0, int materials = 0, bool checklist = false}) => {
  'checklistTemplateId': 'tpl-solar',
  'diagnosisCode': diagnosis,
  'diagnosisNote': null,
  'arrivals': arrivals,
  'beforePhotos': before,
  'afterPhotos': after,
  'testPhotos': test,
  'materials': materials,
  'checklistRecorded': checklist,
  'testMethod': null,
  'submissions': 0,
  'lastReturnReasons': <String>[],
  'lastReturnNote': null,
};

/// Seeded vocabulary as the pull returns it (deploy/seed/reference-data.json, trimmed).
final Map<String, dynamic> referenceJson = {
  'diagnosisCodes': [
    {'code': 'LED_MODULE_FAIL', 'nameAr': 'عطل وحدة الليد', 'nameEn': 'LED module failure', 'countsAsFailure': true, 'requiresMaterial': true, 'expectedItems': ['LED-MOD-50W'], 'powerSources': ['GRID', 'SOLAR', 'HYBRID'], 'path': 'REPAIR'},
    {'code': 'BATTERY_FAIL', 'nameAr': 'عطل البطارية', 'nameEn': 'Battery failure', 'countsAsFailure': true, 'requiresMaterial': true, 'expectedItems': [], 'powerSources': ['SOLAR', 'HYBRID'], 'path': 'REPAIR'},
    {'code': 'FUSE', 'nameAr': 'فيوز', 'nameEn': 'Fuse', 'countsAsFailure': true, 'requiresMaterial': true, 'expectedItems': [], 'powerSources': ['GRID', 'HYBRID'], 'path': 'REPAIR'},
    {'code': 'THEFT', 'nameAr': 'سرقة', 'nameEn': 'Theft', 'countsAsFailure': false, 'requiresMaterial': false, 'expectedItems': [], 'powerSources': ['GRID', 'SOLAR', 'HYBRID'], 'path': 'NO_WORK'},
  ],
  'checklistTemplates': [
    {
      'id': 'tpl-solar',
      'code': 'STREET_LIGHT_REPAIR_SOLAR',
      'version': 1,
      'powerSources': ['SOLAR'],
      'items': [
        {'code': 'LAMP_ON', 'textAr': 'المصباح يعمل', 'textEn': 'Lamp on', 'expected': 'YES', 'blocking': true, 'testItem': true},
        {'code': 'EXPOSED_WIRES', 'textAr': 'أسلاك مكشوفة', 'textEn': 'Exposed wires', 'expected': 'NO', 'blocking': true, 'testItem': false},
      ],
      'testMethods': [
        {'code': 'NIGHT', 'textAr': 'اختبار ليلي', 'textEn': 'At night'},
        {'code': 'NOT_TESTED', 'textAr': 'لم يتم الاختبار', 'textEn': 'Not tested'},
      ],
      'evidenceRequirements': {'beforeFirstArrival': 2, 'beforeLaterArrival': 1, 'after': 2, 'test': 1},
    },
  ],
};

final List<Map<String, dynamic>> custodyJson = [
  {'itemId': 'item-led', 'sku': 'LED-MOD-50W', 'nameAr': 'وحدة ليد 50 واط', 'nameEn': 'LED module 50 W', 'unit': 'EA', 'serialTracked': true, 'quantity': 2, 'serials': ['M-0003', 'M-0004']},
  {'itemId': 'item-fuse', 'sku': 'FUSE-10A', 'nameAr': 'فيوز', 'nameEn': 'Fuse', 'unit': 'EA', 'serialTracked': false, 'quantity': 5, 'serials': <String>[]},
];

/// A small JPEG (optionally large) standing in for a camera frame.
Uint8List cameraJpeg({int width = 64, int height = 48, int seed = 1}) {
  final image = img.Image(width: width, height: height);
  img.fill(image, color: img.ColorRgb8(seed * 37 % 256, seed * 91 % 256, 120));
  return img.encodeJpg(image);
}

/// Photo source for tests: returns scripted frames instead of opening the camera.
class FakePhotoSource implements PhotoSource {
  int taken = 0;

  @override
  Future<Uint8List?> capture(BuildContext context, String title) async => cameraJpeg(seed: ++taken);
}

class FakeServer {
  String deviceStatus = 'PENDING_APPROVAL';
  List<Map<String, dynamic>> tasks = [];
  final Map<String, List<int>> uploads = {};
  List<Map<String, dynamic>> recommendations = [];
  String? blockedCode;
  bool offline = false;

  /// When true, pushed field commands update [tasks] the way the server would (state and progress counters).
  bool simulateWorkOrders = false;

  /// Requests that never reached the server (not in [requests]).
  int offlineAttempts = 0;
  final List<http.Request> requests = [];
  List<Map<String, dynamic>> Function(List<dynamic> commands) pushResults = (commands) => [
    for (final c in commands) {'commandId': (c as Map)['commandId'], 'result': 'APPLIED', 'code': null, 'message': null, 'entityId': 'e'},
  ];

  late final MockClient client = MockClient((request) async {
    if (offline) {
      offlineAttempts++;
      throw http.ClientException('offline');
    }

    requests.add(request);
    final path = request.url.path;
    http.Response json(Object body, [int status = 200]) =>
        http.Response.bytes(utf8.encode(jsonEncode(body)), status, headers: {'content-type': 'application/json'});

    if (path == '/sync/v1/devices/register') return json({'deviceId': 'device-1', 'status': 'PENDING_APPROVAL'}, 201);
    if (path == '/sync/v1/devices/current') return json({'deviceId': 'device-1', 'status': deviceStatus, 'reason': null});
    if (blockedCode != null) return json({'code': blockedCode, 'detail': 'blocked'}, 403);
    if (deviceStatus != 'ACTIVE') return json({'code': 'DEVICE_NOT_APPROVED'}, 403);
    if (path == '/sync/v1/pull') {
      return json({
        'serverTime': '2026-09-24T10:05:00Z',
        'cursor': 'c',
        'user': {
          'id': 'u',
          'displayName': 'فني تجريبي',
          'roles': ['TECHNICIAN'],
        },
        'deviceId': 'device-1',
        'workOrders': tasks,
        'reference': referenceJson,
        'custody': custodyJson,
      });
    }

    if (path == '/sync/v1/push') {
      final body = jsonDecode(request.body) as Map<String, dynamic>;
      if (simulateWorkOrders) {
        for (final c in body['commands'] as List<dynamic>) {
          _apply(c as Map<String, dynamic>);
        }
      }

      return json({'serverTime': '2026-09-24T10:05:00Z', 'results': pushResults(body['commands'] as List<dynamic>)});
    }

    if (path.startsWith('/sync/v1/evidence/') && request.method == 'PUT') {
      uploads[path.split('/')[4]] = request.bodyBytes;
      return json({'id': path.split('/')[4], 'state': 'UPLOADED'});
    }

    if (path.startsWith('/sync/v1/tags/')) {
      final token = Uri.decodeComponent(path.split('/').last);
      if (token != '7K3M92QX.4F') return json({'code': 'TAG_NOT_FOUND'}, 404);
      return json({
        'asset': assetJson(),
        'openFaults': 1,
        'myOpenTasks': tasks,
        'recentEvents': [
          {'at': '2026-09-24T09:00:00Z', 'action': 'fault.reported', 'actorName': 'مشرف تجريبي'},
        ],
        'recommendations': recommendations,
      });
    }

    if (path.startsWith('/sync/v1/work-orders/')) {
      final id = path.split('/').last;
      final task = tasks.where((t) => t['id'] == id).firstOrNull;
      return task == null ? json({'code': 'NOT_ASSIGNEE'}, 403) : json(task);
    }

    return json({'code': 'NOT_FOUND'}, 404);
  });
}

extension on FakeServer {
  void _apply(Map<String, dynamic> command) {
    final payload = command['payload'] as Map<String, dynamic>;
    final task = tasks.where((t) => t['id'] == payload['workOrderId']).firstOrNull;
    if (task == null) return;
    final p = (task['progress'] as Map<String, dynamic>?) ?? (task['progress'] = progressJson());
    void inc(String k) => p[k] = (p[k] as int) + 1;
    switch (command['type']) {
      case 'ArriveOnSite':
        task['state'] = 'ON_SITE';
        inc('arrivals');
      case 'AddEvidence':
        inc({'BEFORE': 'beforePhotos', 'AFTER': 'afterPhotos', 'TEST': 'testPhotos'}[payload['kind']]!);
      case 'RecordDiagnosis':
        p['diagnosisCode'] = payload['code'];
      case 'StartWork':
        task['state'] = 'IN_PROGRESS';
      case 'ConsumeMaterial':
        inc('materials');
      case 'RecordChecklist':
        p['checklistRecorded'] = true;
      case 'SubmitWorkOrder' || 'Resubmit':
        task['state'] = 'PENDING_REVIEW';
        inc('submissions');
    }
  }
}

class Harness {
  Harness({DateTime Function()? clock}) {
    photos = FakePhotoSource();
    auth = FakeAuth(store);
    controller = AppController(
      config: testConfig,
      store: store,
      auth: auth,
      deviceInfo: FakeDeviceInfo(),
      openDb: (s) => LocalDb.open(s),
      apiFactory: (deviceId) => ApiClient(testConfig, auth, deviceId, httpClient: server.client),
      pin: PinService(store, iterations: 10),
      clock: clock,
      photoSource: photos,
    );
  }

  late final FakePhotoSource photos;

  final store = MemorySecureStore();
  final server = FakeServer();
  late final FakeAuth auth;
  late final AppController controller;
}
