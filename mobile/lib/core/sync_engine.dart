import 'dart:convert';

import 'api_client.dart';
import 'evidence_queue.dart';
import 'local_db.dart';
import 'models.dart';
import 'offline_grant.dart';
import 'outbox.dart';

class SyncStatus {
  const SyncStatus({required this.online, required this.pending, required this.lastSyncAt, this.photosWaiting = 0, this.held = 0});

  final bool online;
  final int pending;
  final DateTime? lastSyncAt;

  /// Photos taken on the phone whose bytes the server does not have yet.
  final int photosWaiting;

  /// Commands the server holds for a supervisor's decision (quarantine).
  final int held;
}

/// The server's grant signature did not verify with the pinned key: the grant is not used (the old one stays).
class GrantRejectedException implements Exception {
  GrantRejectedException(this.reason);

  final GrantAcceptance reason;
}

/// One sync cycle (docs/lean-pilot/08 §8.6, M4 hardening): push queued commands in order — each with the grant, boot and
/// monotonic clock it was made under and the work-order version the technician saw — ask again about held commands, upload
/// photo bytes (resumable), renew the offline grant, then pull the current tasks, reference data, custody, stock documents
/// (M5) and policies.
/// The server wins: after a pull, the cached tasks are replaced by the server's view; a refused change stays visible.
class SyncEngine {
  SyncEngine(this._api, this._outbox, this._db, {DateTime Function()? clock, required this.grants, required this.deviceId})
    : _clock = clock ?? DateTime.now,
      evidence = EvidenceQueue(_db);

  final ApiClient _api;
  final Outbox _outbox;
  final LocalDb _db;
  final EvidenceQueue evidence;
  final DateTime Function() _clock;
  final GrantStore grants;
  final Future<String?> Function() deviceId;
  bool _online = true;

  static const _tasksKey = 'tasks_json';
  static const _lastSyncKey = 'last_sync_at';
  static const _userKey = 'user_json';
  static const _referenceKey = 'reference_json';
  static const _custodyKey = 'custody_json';
  static const _stockKey = 'stock_json';
  static const _itemsKey = 'items_json';
  static const batchSize = 50;

  SyncStatus get status => SyncStatus(
    online: _online,
    pending: _outbox.pendingCount,
    lastSyncAt: _db.getValue(_lastSyncKey) == null ? null : DateTime.parse(_db.getValue(_lastSyncKey)!),
    photosWaiting: evidence.waitingCount,
    held: _outbox.heldCount,
  );

  FieldReference get cachedReference {
    final json = _db.getValue(_referenceKey);
    return FieldReference.fromJson(json == null ? null : jsonDecode(json) as Map<String, dynamic>);
  }

  List<CustodyItem> get cachedCustody {
    final json = _db.getValue(_custodyKey);
    if (json == null) return [];
    return (jsonDecode(json) as List<dynamic>).map((e) => CustodyItem.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// M5: issues waiting for my acknowledgment, my declared returns, failed parts to declare or bring back.
  TechnicianStock get cachedStock {
    final json = _db.getValue(_stockKey);
    return TechnicianStock.fromJson(json == null ? null : jsonDecode(json) as Map<String, dynamic>?);
  }

  List<StockItemRef> get cachedItems {
    final json = _db.getValue(_itemsKey);
    if (json == null) return [];
    return (jsonDecode(json) as List<dynamic>).map((e) => StockItemRef.fromJson(e as Map<String, dynamic>)).toList();
  }

  List<WorkOrderTask> get cachedTasks {
    final json = _db.getValue(_tasksKey);
    if (json == null) return [];
    return (jsonDecode(json) as List<dynamic>).map((e) => WorkOrderTask.fromJson(e as Map<String, dynamic>)).toList();
  }

  List<String> get cachedRoles {
    final json = _db.getValue(_userKey);
    return json == null ? [] : ((jsonDecode(json) as Map<String, dynamic>)['roles'] as List<dynamic>).cast<String>();
  }

  String? get cachedUserName {
    final json = _db.getValue(_userKey);
    return json == null ? null : (jsonDecode(json) as Map<String, dynamic>)['displayName'] as String;
  }

  Future<SyncStatus>? _running;
  bool _again = false;

  /// One sync at a time: a request during a running sync makes it run once more at the end (new commands are
  /// never sent twice, and never out of order).
  /// Throws [DeviceBlockedException] / [NotSignedInException]; network failures only mark the phone offline.
  Future<SyncStatus> syncNow() {
    if (_running != null) {
      _again = true;
      return _running!;
    }

    return _running = _loop().whenComplete(() => _running = null);
  }

  Future<SyncStatus> _loop() async {
    do {
      _again = false;
      await _once();
    } while (_again && _online);
    return status;
  }

  Future<SyncStatus> _once() async {
    try {
      await _pushAll();
      await _recheckHeld();
      await _uploadPhotos();
      await renewGrant();
      final pull = await _api.pull();
      if (pull.raw['policy'] is Map<String, dynamic>) grants.savePolicy(ClientPolicy.fromJson(pull.raw['policy'] as Map<String, dynamic>));
      _db.setValue(_tasksKey, jsonEncode(pull.raw['workOrders']));
      if (pull.raw['reference'] != null) _db.setValue(_referenceKey, jsonEncode(pull.raw['reference']));
      if (pull.raw['custody'] != null) _db.setValue(_custodyKey, jsonEncode(pull.raw['custody']));
      _db.setValue(_stockKey, jsonEncode(pull.raw['stock']));
      if (pull.raw['items'] != null) _db.setValue(_itemsKey, jsonEncode(pull.raw['items']));
      _db.setValue(_userKey, jsonEncode({'displayName': pull.userName, 'roles': pull.roles}));
      _db.setValue(_lastSyncKey, _clock().toUtc().toIso8601String());
      _online = true;
    } on OfflineException {
      _online = false;
    }

    return status;
  }

  /// Uploads photos whose announcement (AddEvidence) the server APPLIED. A refused announcement means the bytes will
  /// never be accepted: the local copy is dropped and the refusal stays visible on the task.
  Future<void> _uploadPhotos() async {
    final outcomes = {for (final c in _outbox.all()) c.commandId: c};
    for (final photo in evidence.waiting()) {
      final command = outcomes[photo.commandId];
      if (command == null || command.state == OutboxState.pending) continue;
      if (command.state == OutboxState.rejected) {
        evidence.markFailed(photo.evidenceId, command.resultCode ?? 'REJECTED');
        continue;
      }

      final bytes = evidence.bytesOf(photo.evidenceId);
      if (bytes == null) {
        evidence.markFailed(photo.evidenceId, 'MISSING_LOCAL_COPY');
        continue;
      }

      evidence.markAttempt(photo.evidenceId);
      try {
        await _api.putEvidence(photo.evidenceId, bytes);
        evidence.markUploaded(photo.evidenceId);
      } on DeviceBlockedException {
        rethrow;
      } on ApiException catch (e) {
        // 4xx other than a transient server error: the server will not take these bytes.
        if (e.status >= 400 && e.status < 500) {
          evidence.markFailed(photo.evidenceId, e.code);
        }
      }
    }
  }

  /// The phone renews its grant on every successful sync (after sending what was made under the old one), so while it is
  /// connected a technician never reaches the end of the offline period (docs/lean-pilot/05 §5.5).
  Future<void> renewGrant() async {
    final id = await deviceId();
    if (id == null) return;
    final response = await _api.grant();
    final accepted = await grants.accept(response, id, _api.trust, await _api.trust.now());
    if (accepted != GrantAcceptance.accepted) throw GrantRejectedException(accepted);
  }

  Future<void> _pushAll() async {
    while (true) {
      final batch = _outbox.pending().take(batchSize).toList();
      if (batch.isEmpty) return;
      _outbox.markAttempted(batch.map((c) => c.commandId));
      final results = await _api.push(batch.map((c) => c.toPush()).toList(), _outbox.pendingCount - batch.length);
      for (final r in results) {
        _outbox.markResult(r.commandId, r.result, r.code, r.message, r.entityId);
      }
    }
  }

  /// Held (quarantined) commands are sent again with the same command id: the server applies nothing twice and answers with
  /// the supervisor's decision once there is one (DUPLICATE = applied, REJECTED = discarded).
  Future<void> _recheckHeld() async {
    final held = _outbox.held().take(batchSize).toList();
    if (held.isEmpty) return;
    final results = await _api.push(held.map((c) => c.toPush()).toList(), _outbox.pendingCount);
    for (final r in results) {
      _outbox.markResult(r.commandId, r.result, r.code, r.message, r.entityId);
    }
  }
}
