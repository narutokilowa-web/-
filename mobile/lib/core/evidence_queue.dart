import 'dart:typed_data';

import 'local_db.dart';

/// waiting: announced (AddEvidence queued or sent), bytes not yet on the server; uploaded: the server has the bytes
/// (the local copy is deleted); failed: the server refused the announcement or the upload (the local copy is deleted).
enum EvidenceUploadState { waiting, uploaded, failed }

class QueuedPhoto {
  QueuedPhoto(this.evidenceId, this.workOrderId, this.kind, this.sha256, this.sizeBytes, this.capturedAt, this.commandId, this.state, this.attempts, this.error);

  final String evidenceId;
  final String workOrderId;
  final String kind;
  final String sha256;
  final int sizeBytes;
  final DateTime capturedAt;
  final String commandId;
  final EvidenceUploadState state;
  final int attempts;
  final String? error;
}

/// Photos taken in the app, kept (encrypted, in the app's own database) until the server has them (docs/lean-pilot/06).
class EvidenceQueue {
  EvidenceQueue(this._db);

  final LocalDb _db;

  void add({
    required String evidenceId,
    required String workOrderId,
    required String kind,
    required String sha256,
    required Uint8List bytes,
    required DateTime capturedAt,
    required String commandId,
  }) {
    _db.db.execute(
      'INSERT INTO evidence(evidence_id, work_order_id, kind, sha256, size_bytes, captured_at, command_id, state, bytes) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)',
      [evidenceId, workOrderId, kind, sha256, bytes.length, capturedAt.toUtc().toIso8601String(), commandId, EvidenceUploadState.waiting.name, bytes],
    );
  }

  List<QueuedPhoto> waiting() => _query("WHERE state = 'waiting' ORDER BY captured_at");

  List<QueuedPhoto> forWorkOrder(String workOrderId) => _query('WHERE work_order_id = ? ORDER BY captured_at', [workOrderId]);

  int get waitingCount => _db.db.select("SELECT count(*) AS c FROM evidence WHERE state = 'waiting'").first['c'] as int;

  Uint8List? bytesOf(String evidenceId) {
    final rows = _db.db.select('SELECT bytes FROM evidence WHERE evidence_id = ?', [evidenceId]);
    return rows.isEmpty ? null : rows.first['bytes'] as Uint8List?;
  }

  void markAttempt(String evidenceId) => _db.db.execute('UPDATE evidence SET attempts = attempts + 1 WHERE evidence_id = ?', [evidenceId]);

  /// The server has the bytes: the phone's copy is deleted.
  void markUploaded(String evidenceId) =>
      _db.db.execute("UPDATE evidence SET state = 'uploaded', bytes = NULL, error = NULL WHERE evidence_id = ?", [evidenceId]);

  void markFailed(String evidenceId, String error) =>
      _db.db.execute("UPDATE evidence SET state = 'failed', bytes = NULL, error = ? WHERE evidence_id = ?", [error, evidenceId]);

  List<QueuedPhoto> _query(String tail, [List<Object?> args = const []]) => _db.db
      .select('SELECT evidence_id, work_order_id, kind, sha256, size_bytes, captured_at, command_id, state, attempts, error FROM evidence $tail', args)
      .map(
        (r) => QueuedPhoto(
          r['evidence_id'] as String,
          r['work_order_id'] as String,
          r['kind'] as String,
          r['sha256'] as String,
          r['size_bytes'] as int,
          DateTime.parse(r['captured_at'] as String),
          r['command_id'] as String,
          EvidenceUploadState.values.byName(r['state'] as String),
          r['attempts'] as int,
          r['error'] as String?,
        ),
      )
      .toList();
}
