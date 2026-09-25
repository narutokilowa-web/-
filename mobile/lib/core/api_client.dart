import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:crypto/crypto.dart';
import 'package:http/http.dart' as http;

import 'auth.dart';
import 'config.dart';
import 'device_trust.dart';
import 'models.dart';

class ApiException implements Exception {
  ApiException(this.status, this.code, [this.detail, this.details = const {}]);

  final int status;
  final String code;
  final String? detail;

  /// Machine-readable extras of the problem (e.g. receivedBytes of a resumable upload).
  final Map<String, String> details;

  @override
  String toString() => 'ApiException($status, $code)';
}

/// The server said this phone may no longer act (revoked or rejected). Handled by wiping the app's own data.
class DeviceBlockedException extends ApiException {
  DeviceBlockedException(super.status, super.code, [super.detail]);
}

class NotSignedInException implements Exception {}

/// 426: this app version is below the server's `min_app_version` policy. Nothing is sent until the app is updated.
class UpgradeRequiredException extends ApiException {
  UpgradeRequiredException(this.minAppVersion) : super(426, 'UPGRADE_REQUIRED');

  final String minAppVersion;
}

/// Network unreachable: the phone is offline; nothing was lost.
class OfflineException implements Exception {
  OfflineException(this.cause);

  final Object cause;
}

/// Thin client for the API. Every request carries the access token, the app version and, once registered, the device id
/// and the phone's monotonic clock (trusted time, docs/lean-pilot/05 §5.7). Writes through the single write path and grant
/// requests are signed with the Keystore device key (05 §5.4). The server checks all of it on every request.
class ApiClient {
  ApiClient(this._config, this._auth, this._deviceId, {http.Client? httpClient, DeviceTrust? trust, this.chunkSize = 256 * 1024})
    : _http = httpClient ?? http.Client(),
      trust = trust ?? PlatformDeviceTrust();

  final AppConfig _config;
  final AuthService _auth;
  final Future<String?> Function() _deviceId;
  final http.Client _http;
  final DeviceTrust trust;

  /// Resumable photo upload: bytes per request (small enough for a weak mobile connection).
  final int chunkSize;

  /// USER_DISABLED: the employee was offboarded — handled like a revoked phone (the app forgets its own data).
  static const blockedCodes = {'DEVICE_REVOKED', 'DEVICE_REJECTED', 'DEVICE_NOT_FOUND', 'USER_DISABLED'};

  static const _signedPaths = {'/sync/v1/push', '/sync/v1/grant'};

  /// Last server time seen, anchored to the monotonic clock: signature timestamps follow the server's clock, so a wrong
  /// phone clock does not block sync.
  (DateTime, MonotonicTime)? _serverAnchor;

  /// Called with every server time the API reports (the grant store keeps its own anchor from it).
  void Function(DateTime serverTime, MonotonicTime at)? onServerTime;

  Future<Map<String, dynamic>> registerDevice({required String model, required String osVersion}) async =>
      (await _send(
            'POST',
            '/sync/v1/devices/register',
            body: {
              'platform': 'ANDROID',
              'model': model,
              'osVersion': osVersion,
              'appVersion': _config.appVersion,
              'publicKey': await trust.publicKey(),
            },
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

  /// A fresh offline grant for this device (signed request). The caller verifies the server's signature before keeping it.
  Future<Map<String, dynamic>> grant() async => (await _send('POST', '/sync/v1/grant', body: const <String, dynamic>{})) as Map<String, dynamic>;

  Future<AssetCard> resolveTag(String token) async =>
      AssetCard.fromJson((await _send('GET', '/sync/v1/tags/${Uri.encodeComponent(token)}')) as Map<String, dynamic>);

  Future<WorkOrderTask> task(String id) async =>
      WorkOrderTask.fromJson((await _send('GET', '/sync/v1/work-orders/$id')) as Map<String, dynamic>);

  /// Resumable upload (M4): asks how many bytes the server already holds, then sends the rest in chunks
  /// (`Content-Range: bytes a-b/total`). A dropped connection loses at most one chunk; the next sync continues from the
  /// server's count. Returns the evidence state.
  Future<String> putEvidence(String evidenceId, List<int> bytes) async {
    final status = (await _send('GET', '/sync/v1/evidence/$evidenceId/upload')) as Map<String, dynamic>;
    if (status['state'] != 'ANNOUNCED') return status['state'] as String;
    var from = (status['receivedBytes'] as num?)?.toInt() ?? 0;
    var state = 'ANNOUNCED';
    while (from < bytes.length) {
      final to = from + chunkSize < bytes.length ? from + chunkSize : bytes.length;
      try {
        final result = (await _send('PUT', '/sync/v1/evidence/$evidenceId/content', bytes: bytes.sublist(from, to), range: (from, to - 1, bytes.length)))
            as Map<String, dynamic>;
        state = result['state'] as String? ?? state;
        from = (result['receivedBytes'] as num?)?.toInt() ?? to;
      } on ApiException catch (e) {
        if (e.code != 'UPLOAD_OFFSET_MISMATCH' || e.details['receivedBytes'] == null) rethrow;
        from = int.parse(e.details['receivedBytes']!); // continue where the server stopped
      }
    }

    return state;
  }

  Future<Object?> _send(
    String method,
    String path, {
    Object? body,
    List<int>? bytes,
    bool withDevice = true,
    bool rawBlocked = false,
    (int, int, int)? range,
    bool retried = false,
  }) async {
    final token = await _auth.accessToken();
    if (token == null) throw NotSignedInException();
    final headers = {'Authorization': 'Bearer $token', 'Accept': 'application/json', 'X-App-Version': _config.appVersion};
    final clock = await trust.now();
    if (withDevice) {
      final device = await _deviceId();
      if (device != null) {
        headers['X-Device-Id'] = device;
        headers['X-Device-Elapsed-Ms'] = '${clock.elapsedMs}';
        headers['X-Device-Boot-Id'] = clock.bootId;
      }
    }

    final text = method == 'POST' ? jsonEncode(body ?? {}) : null;
    if (method == 'POST' && _signedPaths.contains(path)) {
      final timestamp = '${_serverNow(clock).millisecondsSinceEpoch}';
      final nonce = _nonce();
      final message = '$method\n$path\n$timestamp\n$nonce\n${sha256.convert(utf8.encode(text!))}';
      headers['X-Signature-Timestamp'] = timestamp;
      headers['X-Signature-Nonce'] = nonce;
      headers['X-Device-Signature'] = await trust.sign(message);
    }

    final uri = Uri.parse('${_config.apiBaseUrl}$path');
    http.Response response;
    try {
      if (method == 'GET') {
        response = await _http.get(uri, headers: headers).timeout(const Duration(seconds: 20));
      } else if (method == 'PUT') {
        headers['Content-Type'] = 'image/jpeg';
        if (range != null) headers['Content-Range'] = 'bytes ${range.$1}-${range.$2}/${range.$3}';
        response = await _http.put(uri, headers: headers, body: bytes).timeout(const Duration(seconds: 120));
      } else {
        headers['Content-Type'] = 'application/json';
        response = await _http.post(uri, headers: headers, body: text).timeout(const Duration(seconds: 30));
      }
    } on SocketException catch (e) {
      throw OfflineException(e);
    } on TimeoutException catch (e) {
      throw OfflineException(e);
    } on http.ClientException catch (e) {
      throw OfflineException(e);
    }

    final responseText = utf8.decode(response.bodyBytes);
    final json = responseText.isEmpty ? null : jsonDecode(responseText);
    if (json is Map<String, dynamic> && json['serverTime'] is String) {
      _observe(DateTime.parse(json['serverTime'] as String), clock);
    }

    if (response.statusCode >= 200 && response.statusCode < 300) return json;

    final code = json is Map<String, dynamic> ? (json['code'] as String? ?? 'HTTP_${response.statusCode}') : 'HTTP_${response.statusCode}';
    final detail = json is Map<String, dynamic> ? json['detail'] as String? : null;
    final details = json is Map<String, dynamic> && json['details'] is Map
        ? (json['details'] as Map).map((k, v) => MapEntry('$k', '$v'))
        : const <String, String>{};
    if (response.statusCode == 401) {
      await _auth.signOut();
      throw NotSignedInException();
    }

    if (response.statusCode == 426) throw UpgradeRequiredException(details['minAppVersion'] ?? '?');

    // The signature time was too far from the server's: learn the server time once, then sign again.
    if (code == 'SIGNATURE_EXPIRED' && !retried) {
      await _send('GET', '/sync/v1/devices/current', rawBlocked: true);
      return _send(method, path, body: body, bytes: bytes, withDevice: withDevice, rawBlocked: rawBlocked, range: range, retried: true);
    }

    if (!rawBlocked && blockedCodes.contains(code)) throw DeviceBlockedException(response.statusCode, code, detail);
    throw ApiException(response.statusCode, code, detail, details);
  }

  void _observe(DateTime serverTime, MonotonicTime at) {
    _serverAnchor = (serverTime.toUtc(), at);
    onServerTime?.call(serverTime.toUtc(), at);
  }

  DateTime _serverNow(MonotonicTime now) {
    final anchor = _serverAnchor;
    if (anchor != null && anchor.$2.bootId == now.bootId && now.elapsedMs >= anchor.$2.elapsedMs) {
      return anchor.$1.add(Duration(milliseconds: now.elapsedMs - anchor.$2.elapsedMs));
    }

    return DateTime.now().toUtc();
  }

  static final _random = Random.secure();

  static String _nonce() => List.generate(16, (_) => _random.nextInt(256).toRadixString(16).padLeft(2, '0')).join();
}
