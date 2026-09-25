import 'package:flutter_appauth/flutter_appauth.dart';

import 'config.dart';
import 'secure_store.dart';

class SignInCancelled implements Exception {}

/// Identity only (Keycloak, OIDC Authorization Code + PKCE in the system browser). Roles and device trust come
/// from our API. Access tokens live in memory; the refresh token is kept in Keystore-backed storage.
abstract class AuthService {
  Future<bool> hasSession();

  /// Opens the system browser for Keycloak login.
  Future<void> signIn();

  /// A valid access token (refreshed when needed), or null when the user must sign in again.
  Future<String?> accessToken();

  /// Forgets this app's tokens on this phone.
  Future<void> signOut();
}

class AppAuthService implements AuthService {
  AppAuthService(this._config, this._store, [FlutterAppAuth? appAuth]) : _appAuth = appAuth ?? const FlutterAppAuth();

  final AppConfig _config;
  final SecureStore _store;
  final FlutterAppAuth _appAuth;
  String? _accessToken;
  DateTime? _expiresAt;

  static const _scopes = ['openid', 'profile'];

  @override
  Future<bool> hasSession() async => await _store.read(SecureKeys.refreshToken) != null;

  @override
  Future<void> signIn() async {
    try {
      final result = await _appAuth.authorizeAndExchangeCode(
        AuthorizationTokenRequest(
          _config.clientId,
          _config.redirectUri,
          issuer: _config.issuer,
          scopes: _scopes,
          allowInsecureConnections: _config.allowInsecureConnections,
          externalUserAgent: ExternalUserAgent.ephemeralAsWebAuthenticationSession,
        ),
      );
      await _keep(result.accessToken, result.accessTokenExpirationDateTime, result.refreshToken);
    } on FlutterAppAuthUserCancelledException {
      throw SignInCancelled();
    }
  }

  @override
  Future<String?> accessToken() async {
    if (_accessToken != null && _expiresAt != null && DateTime.now().isBefore(_expiresAt!.subtract(const Duration(seconds: 30)))) {
      return _accessToken;
    }

    final refresh = await _store.read(SecureKeys.refreshToken);
    if (refresh == null) return null;
    try {
      final result = await _appAuth.token(
        TokenRequest(
          _config.clientId,
          _config.redirectUri,
          issuer: _config.issuer,
          refreshToken: refresh,
          scopes: _scopes,
          allowInsecureConnections: _config.allowInsecureConnections,
        ),
      );
      await _keep(result.accessToken, result.accessTokenExpirationDateTime, result.refreshToken ?? refresh);
      return _accessToken;
    } on FlutterAppAuthPlatformException {
      // Refresh token expired, revoked (user disabled, logout-all) or rotated elsewhere: sign in again.
      await signOut();
      return null;
    }
  }

  @override
  Future<void> signOut() async {
    _accessToken = null;
    _expiresAt = null;
    await _store.delete(SecureKeys.refreshToken);
  }

  Future<void> _keep(String? access, DateTime? expiresAt, String? refresh) async {
    _accessToken = access;
    _expiresAt = expiresAt ?? DateTime.now().add(const Duration(minutes: 4));
    if (refresh != null) await _store.write(SecureKeys.refreshToken, refresh);
  }
}
