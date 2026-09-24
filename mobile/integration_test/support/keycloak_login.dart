import 'dart:convert';
import 'dart:math';

import 'package:crypto/crypto.dart';
import 'package:http/http.dart' as http;
import 'package:mfop_field/core/auth.dart';
import 'package:mfop_field/core/secure_store.dart';

/// TEST ONLY. Performs the real Keycloak Authorization Code + PKCE flow over HTTP (login form → code → token),
/// because an automated test cannot type into the phone's system browser. Tokens, audience, roles and device checks
/// are therefore exactly the ones the app gets after a browser login.
class KeycloakPkceLogin {
  KeycloakPkceLogin({required this.issuer, required this.clientId, required this.redirectUri, required this.password});

  final String issuer;
  final String clientId;
  final String redirectUri;
  final String password;

  Future<Map<String, dynamic>> tokensFor(String username) async {
    final verifier = _random(64);
    final challenge = base64UrlEncode(sha256.convert(ascii.encode(verifier)).bytes).replaceAll('=', '');
    final cookies = <String, String>{};
    final client = http.Client();
    try {
      final authUri = Uri.parse('$issuer/protocol/openid-connect/auth').replace(
        queryParameters: {
          'client_id': clientId,
          'redirect_uri': redirectUri,
          'response_type': 'code',
          'scope': 'openid profile',
          'state': _random(16),
          'code_challenge': challenge,
          'code_challenge_method': 'S256',
        },
      );
      final page = await _send(client, http.Request('GET', authUri), cookies);
      final action =
          RegExp(r'id="kc-form-login"[^>]*action="([^"]+)"').firstMatch(page.body)?.group(1) ??
          RegExp(r'action="([^"]+login-actions/authenticate[^"]*)"').firstMatch(page.body)?.group(1);
      if (action == null) throw StateError('Keycloak login form not found (${page.statusCode}).');

      final post = http.Request('POST', Uri.parse(action.replaceAll('&amp;', '&')))
        ..bodyFields = {'username': username, 'password': password, 'credentialId': ''};
      final redirect = await _send(client, post, cookies);
      final location = redirect.headers['location'];
      final code = location == null ? null : Uri.parse(location).queryParameters['code'];
      if (code == null) throw StateError('Login for $username failed (${redirect.statusCode}).');

      final token = await client.post(
        Uri.parse('$issuer/protocol/openid-connect/token'),
        body: {
          'grant_type': 'authorization_code',
          'client_id': clientId,
          'code': code,
          'redirect_uri': redirectUri,
          'code_verifier': verifier,
        },
      );
      if (token.statusCode != 200) throw StateError('Token exchange failed: ${token.statusCode} ${token.body}');
      return jsonDecode(token.body) as Map<String, dynamic>;
    } finally {
      client.close();
    }
  }

  static Future<http.Response> _send(http.Client client, http.Request request, Map<String, String> cookies) async {
    request.followRedirects = false;
    if (cookies.isNotEmpty) request.headers['cookie'] = cookies.entries.map((e) => '${e.key}=${e.value}').join('; ');
    final response = await http.Response.fromStream(await client.send(request));
    final setCookie = response.headers['set-cookie'];
    if (setCookie != null) {
      for (final m in RegExp(r'(?:^|,\s*)([A-Za-z0-9_]+)=([^;,]*)').allMatches(setCookie)) {
        cookies[m.group(1)!] = m.group(2)!;
      }
    }

    return response;
  }

  static String _random(int length) {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~';
    final r = Random.secure();
    return List.generate(length, (_) => chars[r.nextInt(chars.length)]).join();
  }
}

/// The app's AuthService for the automated device test: signs in [username] through [KeycloakPkceLogin]
/// and keeps the refresh token in the app's real Keystore-backed storage.
class TestKeycloakAuth implements AuthService {
  TestKeycloakAuth(this.login, this.username, this.store);

  final KeycloakPkceLogin login;
  final String username;
  final SecureStore store;
  String? _access;

  @override
  Future<bool> hasSession() async => await store.read(SecureKeys.refreshToken) != null;

  @override
  Future<void> signIn() async {
    final tokens = await login.tokensFor(username);
    _access = tokens['access_token'] as String;
    await store.write(SecureKeys.refreshToken, tokens['refresh_token'] as String);
  }

  @override
  Future<String?> accessToken() async => await hasSession() ? _access : null;

  @override
  Future<void> signOut() async {
    _access = null;
    await store.delete(SecureKeys.refreshToken);
  }
}
