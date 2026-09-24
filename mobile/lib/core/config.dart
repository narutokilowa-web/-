/// Build-time configuration (--dart-define). Nothing about a real municipality is hard-coded here.
///
/// Development on a USB-connected phone uses `adb reverse tcp:5080 tcp:5080` and `adb reverse tcp:8080 tcp:8080`,
/// so the phone reaches the laptop's API and Keycloak as `localhost` (docs/lean-pilot/01 §1.2).
class AppConfig {
  const AppConfig({
    required this.apiBaseUrl,
    required this.issuer,
    required this.clientId,
    required this.redirectUri,
    required this.environment,
    required this.appVersion,
    required this.allowInsecureConnections,
    required this.appLockMinutes,
  });

  factory AppConfig.fromEnvironment() => const AppConfig(
    apiBaseUrl: String.fromEnvironment('MFOP_API_URL', defaultValue: 'http://localhost:5080'),
    issuer: String.fromEnvironment('MFOP_ISSUER', defaultValue: 'http://localhost:8080/realms/mfop'),
    clientId: String.fromEnvironment('MFOP_CLIENT_ID', defaultValue: 'mfop-mobile'),
    redirectUri: String.fromEnvironment('MFOP_REDIRECT_URI', defaultValue: 'mfop://oauth-callback'),
    environment: String.fromEnvironment('MFOP_ENV', defaultValue: 'DEV'),
    appVersion: String.fromEnvironment('MFOP_APP_VERSION', defaultValue: '0.2.0'),
    // Only the local development stack is plain HTTP (localhost via adb reverse). Pilot builds use HTTPS (M8).
    allowInsecureConnections: bool.fromEnvironment('MFOP_ALLOW_HTTP', defaultValue: true),
    appLockMinutes: int.fromEnvironment('MFOP_APP_LOCK_MINUTES', defaultValue: 5),
  );

  final String apiBaseUrl;
  final String issuer;
  final String clientId;
  final String redirectUri;
  final String environment;
  final String appVersion;
  final bool allowInsecureConnections;

  /// Lock with the app PIN after this long in the background (policy `app_lock_minutes`, default 5).
  final int appLockMinutes;
}
