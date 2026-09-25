import 'dart:convert';

import 'api_client.dart';
import 'evidence_queue.dart';
import 'local_db.dart';
import 'models.dart';
import 'outbox.dart';

class SyncStatus {
  const SyncStatus({required this.online, required this.pending, required this.lastSyncAt, this.photosWaiting = 0});

  final bool online;
  final int pending;
  final DateTime? lastSyncAt;

  /// Photos taken on the phone whose bytes the server does not have yet.
  final int photosWaiting;
}

/// One sync cycle (docs/lean-pilot/08 §8.6): push queued commands in order, upload the bytes of photos whose
/// AddEvidence the server accepted, then pull the current task set, reference data and own custody.
/// The server wins: after a pull, the cached tasks are replaced by the server's view.
/// No conflict resolution, offline grants or resumable uploads yet (M4).
class SyncEngine {
  SyncEngine(this._api, this._outbox, this._db, {DateTime Function()? clock}) : _clock = clock ?? DateTime.now, evidence = EvidenceQueue(_db);

  final ApiClient _api;
  final Outbox _outbox;
  final LocalDb _db;
  final EvidenceQueue evidence;
  final DateTime Function() _clock;
  bool _online = true;

  static const _tasksKey = 'tasks_json';
  static const _lastSyncKey = 'last_sync_at';
  static const _userKey = 'user_json';
  static const _referenceKey = 'reference_json';
  static const _custodyKey = 'custody_json';
  static const batchSize = 50;

  SyncStatus get status => SyncStatus(
    online: _online,
    pending: _outbox.pendingCount,
    lastSyncAt: _db.getValue(_lastSyncKey) == null ? null : DateTime.parse(_db.getValue(_lastSyncKey)!),
    photosWaiting: evidence.waitingCount,
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
      await _uploadPhotos();
      final pull = await _api.pull();
      _db.setValue(_tasksKey, jsonEncode(pull.raw['workOrders']));
      if (pull.raw['reference'] != null) _db.setValue(_referenceKey, jsonEncode(pull.raw['reference']));
      if (pull.raw['custody'] != null) _db.setValue(_custodyKey, jsonEncode(pull.raw['custody']));
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

  Future<void> _pushAll() async {
    while (true) {
      final batch = _outbox.pending().take(batchSize).toList();
      if (batch.isEmpty) return;
      _outbox.markAttempted(batch.map((c) => c.commandId));
      final results = await _api.push(
        batch
            .map(
              (c) => {'commandId': c.commandId, 'type': c.type, 'payload': c.payload, 'deviceTime': c.createdAt.toUtc().toIso8601String()},
            )
            .toList(),
        _outbox.pendingCount - batch.length,
      );
      for (final r in results) {
        _outbox.markResult(r.commandId, r.result, r.code, r.message, r.entityId);
      }
    }
  }
}
