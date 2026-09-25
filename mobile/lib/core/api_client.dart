import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;

import 'auth.dart';
import 'config.dart';
import 'models.dart';

class ApiException implements Exception {
  ApiException(this.status, this.code, [this.detail]);

  final int status;
  final String code;
  final String? detail;

  @override
  String toString() => 'ApiException($status, $code)';
}

/// The server said this phone may no longer act (revoked or rejected). Handled by wiping the app's own data.
class DeviceBlockedException extends ApiException {
  DeviceBlockedException(super.status, super.code, [super.detail]);
}

class NotSignedInException implements Exception {}

/// Network unreachable: the phone is offline; nothing was lost.
class OfflineException implements Exception {
  OfflineException(this.cause);

  final Object cause;
}

/// Thin client for the API. Every request carries the access token and, once registered, the device id
/// (header X-Device-Id); the server checks both on every request.
class ApiClient {
  ApiClient(this._config, this._auth, this._deviceId, {http.Client? httpClient}) : _http = httpClient ?? http.Client();

  final AppConfig _config;
  final AuthService _auth;
  final Future<String?> Function() _deviceId;
  final http.Client _http;

  static const blockedCodes = {'DEVICE_REVOKED', 'DEVICE_REJECTED', 'DEVICE_NOT_FOUND'};

  Future<Map<String, dynamic>> registerDevice({required String model, required String osVersion}) async =>
      (await _send(
            'POST',
            '/sync/v1/devices/register',
            body: {'platform': 'ANDROID', 'model': model, 'osVersion': osVersion, 'appVersion': _config.appVersion},
            withDevice: false,
          ))
          as Map<String, dynamic>;

  /// Own device status; answers even while PENDING / REJECTED / REVOKED (so the phone can react).
  Future<String> deviceStatus() async =>
      ((await _send('GET', '/sync/v1/devices/current', rawBlocked: true)) as Map<String, dynamic>)['status'] as String;

  Future<PullResult> pull() async => PullResult.fromJson((await _send('GET', '/sync/v1/pull')) as Map<String, dynamic>);

  Future<List<PushResult>> push(List<Map<String, dynamic>> commands, int pending) async {
    final body = (await _send('POST', '/sync/v1/push', body: {'commands': commands, 'pendingCommands': pending})) as Map<String, dynamic>;
    return (body['results'] as List<dynamic>).map((e) => PushResult.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<AssetCard> resolveTag(String token) async =>
      AssetCard.fromJson((await _send('GET', '/sync/v1/tags/${Uri.encodeComponent(token)}')) as Map<String, dynamic>);

  Future<WorkOrderTask> task(String id) async =>
      WorkOrderTask.fromJson((await _send('GET', '/sync/v1/work-orders/$id')) as Map<String, dynamic>);

  /// Streams one photo's bytes; the server hashes them and compares with the announced SHA-256. Returns the evidence state.
  Future<String> putEvidence(String evidenceId, List<int> bytes) async {
    final result = await _send('PUT', '/sync/v1/evidence/$evidenceId/content', bytes: bytes);
    return (result as Map<String, dynamic>?)?['state'] as String? ?? 'UPLOADED';
  }

  Future<Object?> _send(String method, String path, {Object? body, List<int>? bytes, bool withDevice = true, bool rawBlocked = false}) async {
    final token = await _auth.accessToken();
    if (token == null) throw NotSignedInException();
    final headers = {'Authorization': 'Bearer $token', 'Accept': 'application/json'};
    if (withDevice) {
      final device = await _deviceId();
      if (device != null) headers['X-Device-Id'] = device;
    }

    final uri = Uri.parse('${_config.apiBaseUrl}$path');
    http.Response response;
    try {
      if (method == 'GET') {
        response = await _http.get(uri, headers: headers).timeout(const Duration(seconds: 20));
      } else if (method == 'PUT') {
        headers['Content-Type'] = 'image/jpeg';
        response = await _http.put(uri, headers: headers, body: bytes).timeout(const Duration(seconds: 120));
      } else {
        headers['Content-Type'] = 'application/json';
        response = await _http.post(uri, headers: headers, body: jsonEncode(body ?? {})).timeout(const Duration(seconds: 30));
      }
    } on SocketException catch (e) {
      throw OfflineException(e);
    } on TimeoutException catch (e) {
      throw OfflineException(e);
    } on http.ClientException catch (e) {
      throw OfflineException(e);
    }

    final text = utf8.decode(response.bodyBytes);
    final json = text.isEmpty ? null : jsonDecode(text);
    if (response.statusCode >= 200 && response.statusCode < 300) return json;

    final code = json is Map<String, dynamic> ? (json['code'] as String? ?? 'HTTP_${response.statusCode}') : 'HTTP_${response.statusCode}';
    final detail = json is Map<String, dynamic> ? json['detail'] as String? : null;
    if (response.statusCode == 401) {
      await _auth.signOut();
      throw NotSignedInException();
    }

    if (!rawBlocked && blockedCodes.contains(code)) throw DeviceBlockedException(response.statusCode, code, detail);
    throw ApiException(response.statusCode, code, detail);
  }
}
