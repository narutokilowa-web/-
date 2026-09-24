import 'dart:convert';

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
class FakeServer {
  String deviceStatus = 'PENDING_APPROVAL';
  List<Map<String, dynamic>> tasks = [];
  String? blockedCode;
  bool offline = false;
  final List<http.Request> requests = [];
  List<Map<String, dynamic>> Function(List<dynamic> commands) pushResults = (commands) => [
    for (final c in commands) {'commandId': (c as Map)['commandId'], 'result': 'APPLIED', 'code': null, 'message': null, 'entityId': 'e'},
  ];

  late final MockClient client = MockClient((request) async {
    requests.add(request);
    if (offline) throw http.ClientException('offline');
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
      });
    }

    if (path == '/sync/v1/push') {
      final body = jsonDecode(request.body) as Map<String, dynamic>;
      return json({'serverTime': '2026-09-24T10:05:00Z', 'results': pushResults(body['commands'] as List<dynamic>)});
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

class Harness {
  Harness({DateTime Function()? clock}) {
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
    );
  }

  final store = MemorySecureStore();
  final server = FakeServer();
  late final FakeAuth auth;
  late final AppController controller;
}
