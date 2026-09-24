import 'dart:convert';

import 'package:uuid/uuid.dart';

import 'local_db.dart';

enum OutboxState { pending, sent, rejected }

class OutboxItem {
  OutboxItem(
    this.commandId,
    this.seq,
    this.type,
    this.payload,
    this.createdAt,
    this.state,
    this.attempts,
    this.resultCode,
    this.resultMessage,
  );

  final String commandId;
  final int seq;
  final String type;
  final Map<String, dynamic> payload;
  final DateTime createdAt;
  final OutboxState state;
  final int attempts;
  final String? resultCode;
  final String? resultMessage;
}

/// Every write from the phone is a command in this persisted, encrypted queue (docs/lean-pilot/08 §8.6).
/// Commands keep their order and their command id (UUIDv7) across retries, so the server applies each one at most once.
class Outbox {
  Outbox(this._db, {DateTime Function()? clock}) : _clock = clock ?? DateTime.now;

  final LocalDb _db;
  final DateTime Function() _clock;
  static const _uuid = Uuid();

  String enqueue(String type, Map<String, dynamic> payload) {
    final id = _uuid.v7();
    final next = (_db.db.select('SELECT COALESCE(MAX(seq), 0) + 1 AS n FROM outbox').first['n'] as int);
    _db.db.execute('INSERT INTO outbox(command_id, seq, type, payload, created_at, state) VALUES (?, ?, ?, ?, ?, ?)', [
      id,
      next,
      type,
      jsonEncode(payload),
      _clock().toUtc().toIso8601String(),
      OutboxState.pending.name,
    ]);
    return id;
  }

  List<OutboxItem> pending() => _query("WHERE state = 'pending' ORDER BY seq");

  List<OutboxItem> all() => _query('ORDER BY seq DESC');

  int get pendingCount => _db.db.select("SELECT count(*) AS c FROM outbox WHERE state = 'pending'").first['c'] as int;

  void markAttempted(Iterable<String> commandIds) {
    for (final id in commandIds) {
      _db.db.execute('UPDATE outbox SET attempts = attempts + 1 WHERE command_id = ?', [id]);
    }
  }

  /// APPLIED and DUPLICATE are both success (DUPLICATE: the server had already applied it).
  void markResult(String commandId, String result, String? code, String? message, String? entityId) {
    final state = result == 'REJECTED' ? OutboxState.rejected : OutboxState.sent;
    _db.db.execute('UPDATE outbox SET state = ?, result_code = ?, result_message = ?, entity_id = ? WHERE command_id = ?', [
      state.name,
      code ?? result,
      message,
      entityId,
      commandId,
    ]);
  }

  List<OutboxItem> _query(String tail) => _db.db
      .select('SELECT * FROM outbox $tail')
      .map(
        (r) => OutboxItem(
          r['command_id'] as String,
          r['seq'] as int,
          r['type'] as String,
          jsonDecode(r['payload'] as String) as Map<String, dynamic>,
          DateTime.parse(r['created_at'] as String),
          OutboxState.values.byName(r['state'] as String),
          r['attempts'] as int,
          r['result_code'] as String?,
          r['result_message'] as String?,
        ),
      )
      .toList();
}
