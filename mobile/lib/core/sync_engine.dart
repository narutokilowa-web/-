import 'dart:convert';

import 'api_client.dart';
import 'local_db.dart';
import 'models.dart';
import 'outbox.dart';

class SyncStatus {
  const SyncStatus({required this.online, required this.pending, required this.lastSyncAt});

  final bool online;
  final int pending;
  final DateTime? lastSyncAt;
}

/// One sync cycle (docs/lean-pilot/08 §8.6): push queued commands in order, then pull the current task set.
/// The server wins: after a pull, the cached tasks are replaced by the server's view.
/// M2 has no conflict resolution, offline grants or evidence upload yet (M3/M4).
class SyncEngine {
  SyncEngine(this._api, this._outbox, this._db, {DateTime Function()? clock}) : _clock = clock ?? DateTime.now;

  final ApiClient _api;
  final Outbox _outbox;
  final LocalDb _db;
  final DateTime Function() _clock;
  bool _online = true;

  static const _tasksKey = 'tasks_json';
  static const _lastSyncKey = 'last_sync_at';
  static const _userKey = 'user_json';
  static const batchSize = 50;

  SyncStatus get status => SyncStatus(
    online: _online,
    pending: _outbox.pendingCount,
    lastSyncAt: _db.getValue(_lastSyncKey) == null ? null : DateTime.parse(_db.getValue(_lastSyncKey)!),
  );

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

  /// Throws [DeviceBlockedException] / [NotSignedInException]; network failures only mark the phone offline.
  Future<SyncStatus> syncNow() async {
    try {
      await _pushAll();
      final pull = await _api.pull();
      _db.setValue(_tasksKey, jsonEncode(pull.raw['workOrders']));
      _db.setValue(_userKey, jsonEncode({'displayName': pull.userName, 'roles': pull.roles}));
      _db.setValue(_lastSyncKey, _clock().toUtc().toIso8601String());
      _online = true;
    } on OfflineException {
      _online = false;
    }

    return status;
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
