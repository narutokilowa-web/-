import 'evidence_queue.dart';
import 'models.dart';
import 'outbox.dart';

/// The technician's view of one work order: what the server has recorded (last pull) plus what is queued on the
/// phone and not yet sent. The server re-checks every guard; this only decides which step the app offers next.
class WorkView {
  WorkView({
    required this.task,
    required this.state,
    required this.template,
    required this.diagnosisCode,
    required this.beforePhotos,
    required this.afterPhotos,
    required this.testPhotos,
    required this.materials,
    required this.checklistRecorded,
    required this.submittedLocally,
    required this.rejections,
    this.held = const [],
    required this.photosWaiting,
    required this.photosFailed,
    this.coresToDeclare = const [],
  });

  factory WorkView.of(WorkOrderTask task, FieldReference reference, List<OutboxItem> outbox, List<QueuedPhoto> photos,
      {TechnicianStock stock = const TechnicianStock(), List<StockItemRef> items = const []}) {
    final p = task.progress;
    final mine = outbox.where((c) => c.payload['workOrderId'] == task.id).toList()..sort((a, b) => a.seq.compareTo(b.seq));
    final queued = mine.where((c) => c.state == OutboxState.pending).toList();
    var state = task.state;
    var diagnosis = p?.diagnosisCode;
    var before = p?.beforePhotos ?? 0, after = p?.afterPhotos ?? 0, test = p?.testPhotos ?? 0, materials = p?.materials ?? 0;
    var checklist = p?.checklistRecorded ?? false;
    var submitted = false;
    for (final c in queued) {
      switch (c.type) {
        case 'ArriveOnSite':
          state = 'ON_SITE';
        case 'StartWork':
          state = 'IN_PROGRESS';
        case 'RecordDiagnosis':
          diagnosis = c.payload['code'] as String?;
        case 'AddEvidence':
          switch (c.payload['kind']) {
            case 'BEFORE':
              before++;
            case 'AFTER':
              after++;
            case 'TEST':
              test++;
          }
        case 'ConsumeMaterial':
          materials++;
        case 'RecordChecklist':
          checklist = true;
        case 'SubmitWorkOrder' || 'Resubmit':
          state = 'PENDING_REVIEW';
          submitted = true;
      }
    }

    return WorkView(
      task: task,
      state: state,
      template: reference.template(p?.checklistTemplateId),
      diagnosisCode: diagnosis,
      beforePhotos: before,
      afterPhotos: after,
      testPhotos: test,
      materials: materials,
      checklistRecorded: checklist,
      submittedLocally: submitted,
      rejections: mine.where((c) => c.state == OutboxState.rejected).map((c) => Rejection(c.type, c.resultCode ?? 'REJECTED', c.createdAt)).toList(),
      held: mine.where((c) => c.state == OutboxState.held).map((c) => Rejection(c.type, c.resultCode ?? 'QUARANTINED', c.createdAt)).toList(),
      photosWaiting: photos.where((x) => x.state == EvidenceUploadState.waiting).length,
      photosFailed: photos.where((x) => x.state == EvidenceUploadState.failed).length,
      coresToDeclare: pendingCoreObligations(task.id, stock, items, outbox),
    );
  }

  final WorkOrderTask task;

  /// Server state, advanced by queued transitions (ArriveOnSite, StartWork, Submit/Resubmit).
  final String state;
  final ChecklistTemplate? template;
  final String? diagnosisCode;
  final int beforePhotos;
  final int afterPhotos;
  final int testPhotos;
  final int materials;
  final bool checklistRecorded;
  final bool submittedLocally;
  final List<Rejection> rejections;

  /// Commands the server holds for a supervisor (quarantine): neither applied nor lost.
  final List<Rejection> held;
  final int photosWaiting;
  final int photosFailed;

  /// M5 (W9): failed parts of core-return items consumed on this work order that are not declared yet.
  final List<CoreObligation> coresToDeclare;

  EvidenceRequirements get requirements => template?.evidence ?? const EvidenceRequirements(beforeFirstArrival: 2, beforeLaterArrival: 1, after: 2, test: 1);

  bool get isReturned => state == 'RETURNED';
  bool get canArrive => state == 'ASSIGNED';
  bool get onSite => state == 'ON_SITE';
  bool get inProgress => state == 'IN_PROGRESS';
  bool get canTakeBefore => onSite;
  bool get canDiagnose => onSite || inProgress;
  bool get beforeDone => beforePhotos >= requirements.beforeFirstArrival;
  bool get canStart => onSite && diagnosisCode != null && beforeDone;
  bool get canRecordWork => inProgress || isReturned;
  bool get testDone => testPhotos >= requirements.test;
  bool get afterDone => afterPhotos >= requirements.after;

  /// Every step the server requires for W9 / W12 appears done locally (the server still decides).
  bool get readyToSubmit =>
      (inProgress || isReturned) && checklistRecorded && testDone && afterDone && diagnosisCode != null && coresToDeclare.isEmpty;
  bool get waitingForReview => state == 'PENDING_REVIEW';
}

class Rejection {
  Rejection(this.commandType, this.code, this.at);

  final String commandType;
  final String code;
  final DateTime at;
}

/// A failed part the technician must declare (04 §4.11): named by the consumption that opened it (the movement id the phone
/// generated, and the installed serial when one consumption installed several units), so it can be declared offline before
/// the phone has ever pulled the server's core-return record.
class CoreObligation {
  CoreObligation({
    required this.movementId,
    required this.sku,
    required this.nameAr,
    required this.nameEn,
    required this.serialTracked,
    this.coreReturnId,
    this.installedSerial,
    this.expectedSerial,
  });

  final String movementId;
  final String? coreReturnId;
  final String? installedSerial;
  final String? expectedSerial;
  final String sku;
  final String nameAr;
  final String nameEn;
  final bool serialTracked;

  bool matches(Map<String, dynamic> declaration) =>
      (declaration['coreReturnId'] != null && declaration['coreReturnId'] == coreReturnId) ||
      (declaration['movementId'] == movementId && (declaration['installedSerial'] == null || declaration['installedSerial'] == installedSerial));
}

/// The server's OPEN core returns of this work order, plus those opened by consumptions still queued on the phone, minus
/// those already declared (queued or sent). The server decides; this only drives what the app asks for before submission.
List<CoreObligation> pendingCoreObligations(String workOrderId, TechnicianStock stock, List<StockItemRef> items, List<OutboxItem> outbox) {
  final obligations = <CoreObligation>[
    for (final c in stock.coreReturns.where((c) => c.workOrderId == workOrderId && c.state == 'OPEN'))
      CoreObligation(
        movementId: c.consumeMovementId,
        coreReturnId: c.id,
        installedSerial: c.installedSerial,
        expectedSerial: c.expectedSerial,
        sku: c.sku,
        nameAr: c.nameAr,
        nameEn: c.nameEn,
        serialTracked: c.serialTracked,
      ),
  ];
  final known = stock.coreReturns.map((c) => c.consumeMovementId).toSet();
  final mine = outbox.where((c) => c.payload['workOrderId'] == workOrderId).toList()..sort((a, b) => a.seq.compareTo(b.seq));
  for (final c in mine.where((c) => c.type == 'ConsumeMaterial' && c.state == OutboxState.pending)) {
    final movementId = c.payload['movementId'] as String?;
    final item = items.where((i) => i.id == c.payload['itemId']).firstOrNull;
    if (movementId == null || item == null || !item.coreReturnRequired || known.contains(movementId)) continue;
    final serials = ((c.payload['serials'] as List<dynamic>?) ?? const []).cast<String>();
    for (final serial in item.serialTracked ? serials.map<String?>((s) => s) : const <String?>[null]) {
      obligations.add(CoreObligation(
        movementId: movementId,
        installedSerial: serial,
        sku: item.sku,
        nameAr: item.nameAr,
        nameEn: item.nameEn,
        serialTracked: item.serialTracked,
      ));
    }
  }

  final declared = mine.where((c) => c.type == 'DeclareCoreReturn' && c.state != OutboxState.rejected).map((c) => c.payload).toList();
  return obligations.where((o) => !declared.any(o.matches)).toList();
}

/// Issues waiting for my answer (ISSUED), without those I already acknowledged or disputed on the phone.
List<StockIssueView> issuesToAnswer(TechnicianStock stock, List<OutboxItem> outbox) {
  final answered = outbox
      .where((c) => (c.type == 'AcknowledgeStockIssue' || c.type == 'DisputeStockIssue') && c.state != OutboxState.rejected)
      .map((c) => c.payload['issueId'])
      .toSet();
  return stock.issues.where((i) => i.state == 'ISSUED' && !answered.contains(i.id)).toList();
}
