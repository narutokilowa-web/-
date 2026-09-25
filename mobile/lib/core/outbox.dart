import 'dart:convert';

import 'package:uuid/uuid.dart';

import 'local_db.dart';

/// held: the server quarantined the command (a supervisor decides); the phone asks again on each sync.
enum OutboxState { pending, sent, rejected, held }

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
    this.resultMessage, {
    this.grantId,
    this.elapsedMs,
    this.bootId,
    this.baseVersion,
  });

  final String commandId;
  final int seq;
  final String type;
  final Map<String, dynamic> payload;
  final DateTime createdAt;
  final OutboxState state;
  final int attempts;
  final String? resultCode;
  final String? resultMessage;

  /// M4 (docs/lean-pilot/05 §5.5, §5.7): the grant the command was made under, the phone's monotonic clock then, and the
  /// work-order version the technician saw (the server refuses changes on a stale view instead of overwriting).
  final String? grantId;
  final int? elapsedMs;
  final String? bootId;
  final int? baseVersion;

  Map<String, dynamic> toPush() => {
    'commandId': commandId,
    'type': type,
    'payload': payload,
    'deviceTime': createdAt.toUtc().toIso8601String(),
    'grantId': grantId,
    'elapsedMs': elapsedMs,
    'bootId': bootId,
    'baseVersion': baseVersion,
  };
}

/// The context of a command at the moment it is recorded.
class CommandContext {
  const CommandContext({this.grantId, this.elapsedMs, this.bootId, this.baseVersion});

  final String? grantId;
  final int? elapsedMs;
  final String? bootId;
  final int? baseVersion;
}

/// Every write from the phone is a command in this persisted, encrypted queue (docs/lean-pilot/08 §8.6).
/// Commands keep their order and their command id (UUIDv7) across retries, so the server applies each one at most once.
class Outbox {
  Outbox(this._db, {DateTime Function()? clock}) : _clock = clock ?? DateTime.now;

  final LocalDb _db;
  final DateTime Function() _clock;
  static const _uuid = Uuid();

  /// Ids created on the phone (evidence, movements, checklists) are UUIDv7, like command ids.
  static String newId() => _uuid.v7();

  String enqueue(String type, Map<String, dynamic> payload, [CommandContext context = const CommandContext()]) {
    final id = _uuid.v7();
    final next = (_db.db.select('SELECT COALESCE(MAX(seq), 0) + 1 AS n FROM outbox').first['n'] as int);
    _db.db.execute(
      'INSERT INTO outbox(command_id, seq, type, payload, created_at, state, grant_id, elapsed_ms, boot_id, base_version) '
      'VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)',
      [
        id,
        next,
        type,
        jsonEncode(payload),
        _clock().toUtc().toIso8601String(),
        OutboxState.pending.name,
        context.grantId,
        context.elapsedMs,
        context.bootId,
        context.baseVersion,
      ],
    );
    return id;
  }

  List<OutboxItem> pending() => _query("WHERE state = 'pending' ORDER BY seq");

  /// Quarantined commands whose final decision the phone has not learned yet.
  List<OutboxItem> held() => _query("WHERE state = 'held' ORDER BY seq");

  int get heldCount => _db.db.select("SELECT count(*) AS c FROM outbox WHERE state = 'held'").first['c'] as int;

  List<OutboxItem> all() => _query('ORDER BY seq DESC');

  int get pendingCount => _db.db.select("SELECT count(*) AS c FROM outbox WHERE state = 'pending'").first['c'] as int;

  void markAttempted(Iterable<String> commandIds) {
    for (final id in commandIds) {
      _db.db.execute('UPDATE outbox SET attempts = attempts + 1 WHERE command_id = ?', [id]);
    }
  }

  /// APPLIED and DUPLICATE are both success (DUPLICATE: the server had already applied it). QUARANTINED: held for a supervisor.
  void markResult(String commandId, String result, String? code, String? message, String? entityId) {
    final state = switch (result) {
      'REJECTED' => OutboxState.rejected,
      'QUARANTINED' => OutboxState.held,
      _ => OutboxState.sent,
    };
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
          grantId: r['grant_id'] as String?,
          elapsedMs: r['elapsed_ms'] as int?,
          bootId: r['boot_id'] as String?,
          baseVersion: r['base_version'] as int?,
        ),
      )
      .toList();
}
