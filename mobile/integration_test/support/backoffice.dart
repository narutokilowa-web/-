import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:uuid/uuid.dart';

import 'keycloak_login.dart';

/// TEST ONLY. The web-console users' actions (registrar, approver, supervisor, admin), done through the same API the
/// web console calls, with tokens from the real Keycloak (web client). Keeps the device test self-contained.
class Backoffice {
  Backoffice(this.apiBaseUrl, this.login);

  final String apiBaseUrl;
  final KeycloakPkceLogin login;
  final _tokens = <String, String>{};

  Future<String> _token(String user) async => _tokens[user] ??= (await login.tokensFor(user))['access_token'] as String;

  Future<Map<String, dynamic>> call(String user, String method, String path, [Object? body]) async {
    final headers = {
      'Authorization': 'Bearer ${await _token(user)}',
      'Content-Type': 'application/json',
      'Idempotency-Key': const Uuid().v4(),
    };
    final uri = Uri.parse('$apiBaseUrl$path');
    final response = method == 'GET'
        ? await http.get(uri, headers: headers)
        : await http.post(uri, headers: headers, body: jsonEncode(body ?? {}));
    final decoded = response.body.isEmpty ? <String, dynamic>{} : jsonDecode(utf8.decode(response.bodyBytes));
    if (response.statusCode >= 300) throw StateError('$method $path as $user → ${response.statusCode} ${response.body}');
    return decoded is Map<String, dynamic> ? decoded : {'items': decoded};
  }

  /// A synthetic ACTIVE street light (registrar1 drafts and submits, approver1 approves). Returns the asset JSON.
  Future<Map<String, dynamic>> activeStreetLight(String address) async {
    final draft = await call('registrar1', 'POST', '/api/v1/registrations', {
      'reason': 'BASELINE_SURVEY',
      'attributes': {
        'latitude': 33.512,
        'longitude': 36.291,
        'powerSource': 'SOLAR',
        'solarPanelW': 100,
        'solarBatteryAh': 100,
        'responsibleParty': 'MUNICIPALITY',
        'installedOnPrecision': 'UNKNOWN',
        'observedStatus': 'OPERATIONAL',
        'addressText': address,
      },
    });
    final id = draft['id'] as String;
    await call('registrar1', 'POST', '/api/v1/registrations/$id/submit');
    await call('approver1', 'POST', '/api/v1/registrations/$id/approve', {'note': 'android e2e'});
    final registration = await call('approver1', 'GET', '/api/v1/registrations/$id');
    return call('approver1', 'GET', '/api/v1/assets/${registration['assetId']}');
  }
}
