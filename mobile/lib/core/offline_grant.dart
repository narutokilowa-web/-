import 'dart:convert';

import 'device_trust.dart';
import 'local_db.dart';

/// What the server's policies tell the phone (sent with each grant and each pull). Never hard-coded in the app.
class ClientPolicy {
  const ClientPolicy({required this.offlineGraceHours, required this.appLockMinutes, required this.minAppVersion});

  factory ClientPolicy.fromJson(Map<String, dynamic> j) => ClientPolicy(
    offlineGraceHours: j['offlineGraceHours'] as int,
    appLockMinutes: j['appLockMinutes'] as int,
    minAppVersion: j['minAppVersion'] as String,
  );

  final int offlineGraceHours;
  final int appLockMinutes;
  final String minAppVersion;

  Map<String, dynamic> toJson() => {'offlineGraceHours': offlineGraceHours, 'appLockMinutes': appLockMinutes, 'minAppVersion': minAppVersion};
}

/// A signed offline grant (docs/lean-pilot/05 §5.5) as the phone keeps it: the fields come from the signed payload itself.
class OfflineGrant {
  const OfflineGrant({required this.grantId, required this.deviceId, required this.issuedAt, required this.expiresAt, required this.keyId});

  final String grantId;
  final String deviceId;
  final DateTime issuedAt;
  final DateTime expiresAt;
  final String keyId;

  Map<String, dynamic> toJson() => {
    'grantId': grantId,
    'deviceId': deviceId,
    'issuedAt': issuedAt.toUtc().toIso8601String(),
    'expiresAt': expiresAt.toUtc().toIso8601String(),
    'keyId': keyId,
  };

  factory OfflineGrant.fromJson(Map<String, dynamic> j) => OfflineGrant(
    grantId: j['grantId'] as String,
    deviceId: j['deviceId'] as String,
    issuedAt: DateTime.parse(j['issuedAt'] as String),
    expiresAt: DateTime.parse(j['expiresAt'] as String),
    keyId: j['keyId'] as String,
  );
}

enum GrantAcceptance { accepted, badSignature, keyChanged, wrongDevice }

/// The grant, the pinned server keys and the phone's anchor to the server clock, in the encrypted database.
///
/// Expiry is measured on the phone's monotonic clock from the last server time it saw on this boot, so moving the phone's
/// clock back does not extend a grant. After a restart (no anchor for the new boot) the phone uses its wall clock, but never
/// earlier than the newest server time it has seen. The server re-checks every command anyway (05 §5.5).
class GrantStore {
  GrantStore(this._db);

  final LocalDb _db;

  static const _grantKey = 'offline_grant_json';
  static const _keysKey = 'grant_keys_json';
  static const _anchorKey = 'server_clock_anchor_json';
  static const _trustedKey = 'last_trusted_server_time';
  static const _policyKey = 'client_policy_json';

  OfflineGrant? get current {
    final json = _db.getValue(_grantKey);
    return json == null ? null : OfflineGrant.fromJson(jsonDecode(json) as Map<String, dynamic>);
  }

  ClientPolicy? get policy {
    final json = _db.getValue(_policyKey);
    return json == null ? null : ClientPolicy.fromJson(jsonDecode(json) as Map<String, dynamic>);
  }

  void savePolicy(ClientPolicy policy) => _db.setValue(_policyKey, jsonEncode(policy.toJson()));

  /// A server time just received: anchors the monotonic clock for this boot and raises the "not earlier than" floor.
  void observeServerTime(DateTime serverTime, MonotonicTime at) {
    _db.setValue(_anchorKey, jsonEncode({'serverTime': serverTime.toUtc().toIso8601String(), 'elapsedMs': at.elapsedMs, 'bootId': at.bootId}));
    final floor = _db.getValue(_trustedKey);
    if (floor == null || serverTime.isAfter(DateTime.parse(floor))) _db.setValue(_trustedKey, serverTime.toUtc().toIso8601String());
  }

  /// The phone's best estimate of the server's time now.
  DateTime trustedNow(MonotonicTime at, DateTime wallClock) {
    final anchorJson = _db.getValue(_anchorKey);
    if (anchorJson != null) {
      final anchor = jsonDecode(anchorJson) as Map<String, dynamic>;
      if (anchor['bootId'] == at.bootId && at.elapsedMs >= (anchor['elapsedMs'] as int)) {
        return DateTime.parse(anchor['serverTime'] as String).add(Duration(milliseconds: at.elapsedMs - (anchor['elapsedMs'] as int)));
      }
    }

    final floor = _db.getValue(_trustedKey);
    final wall = wallClock.toUtc();
    return floor != null && DateTime.parse(floor).isAfter(wall) ? DateTime.parse(floor) : wall;
  }

  /// True when there is no grant or it has run out: the app then locks new work until it reconnects.
  bool expired(MonotonicTime at, DateTime wallClock) {
    final grant = current;
    return grant == null || !trustedNow(at, wallClock).isBefore(grant.expiresAt);
  }

  /// Verifies a grant from POST /sync/v1/grant against the server key pinned for its key id (the first key for an id is
  /// pinned on first use, over the signed-in connection) and keeps it. A changed key for a known id is refused.
  Future<GrantAcceptance> accept(Map<String, dynamic> response, String deviceId, DeviceTrust trust, MonotonicTime at) async {
    final keyId = response['keyId'] as String;
    final publicKey = response['publicKey'] as String;
    final pinned = pinnedKeys;
    if (pinned.containsKey(keyId) && pinned[keyId] != publicKey) return GrantAcceptance.keyChanged;

    final signedPayload = response['signedPayload'] as String;
    if (!await trust.verify(publicKey, signedPayload, response['signature'] as String)) return GrantAcceptance.badSignature;
    final payload = jsonDecode(signedPayload) as Map<String, dynamic>;
    if (payload['device_id'] != deviceId || payload['kid'] != keyId) return GrantAcceptance.wrongDevice;

    if (!pinned.containsKey(keyId)) {
      _db.setValue(_keysKey, jsonEncode({...pinned, keyId: publicKey}));
    }

    final grant = OfflineGrant(
      grantId: payload['grant_id'] as String,
      deviceId: deviceId,
      issuedAt: DateTime.parse(payload['issued_at'] as String),
      expiresAt: DateTime.parse(payload['expires_at'] as String),
      keyId: keyId,
    );
    _db.setValue(_grantKey, jsonEncode(grant.toJson()));
    if (response['serverTime'] is String) observeServerTime(DateTime.parse(response['serverTime'] as String), at);
    if (response['policy'] is Map<String, dynamic>) savePolicy(ClientPolicy.fromJson(response['policy'] as Map<String, dynamic>));
    return GrantAcceptance.accepted;
  }

  Map<String, String> get pinnedKeys {
    final json = _db.getValue(_keysKey);
    return json == null ? {} : (jsonDecode(json) as Map<String, dynamic>).cast<String, String>();
  }
}
