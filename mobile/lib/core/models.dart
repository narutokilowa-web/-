/// JSON shapes of the API (backend Contracts), read defensively.
class AssetSummary {
  AssetSummary({
    required this.id,
    required this.assetCode,
    required this.lifecycleState,
    required this.operationalStatus,
    required this.powerSource,
    required this.responsibleParty,
    required this.addressText,
    required this.latitude,
    required this.longitude,
  });

  factory AssetSummary.fromJson(Map<String, dynamic> j) => AssetSummary(
    id: j['id'] as String,
    assetCode: j['assetCode'] as String?,
    lifecycleState: j['lifecycleState'] as String,
    operationalStatus: j['operationalStatus'] as String?,
    powerSource: j['powerSource'] as String,
    responsibleParty: j['responsibleParty'] as String,
    addressText: j['addressText'] as String?,
    latitude: (j['latitude'] as num).toDouble(),
    longitude: (j['longitude'] as num).toDouble(),
  );

  final String id;
  final String? assetCode;
  final String lifecycleState;
  final String? operationalStatus;
  final String powerSource;
  final String responsibleParty;
  final String? addressText;
  final double latitude;
  final double longitude;
}

class WorkOrderTask {
  WorkOrderTask({
    required this.id,
    required this.code,
    required this.state,
    required this.priority,
    required this.slaDueAt,
    required this.note,
    required this.faultCategory,
    required this.faultDescription,
    required this.faultPowerAtInspection,
    required this.assignedAt,
    required this.asset,
    this.progress,
    this.eventSeq = 0,
  });

  factory WorkOrderTask.fromJson(Map<String, dynamic> j) => WorkOrderTask(
    id: j['id'] as String,
    code: j['code'] as String,
    state: j['state'] as String,
    priority: j['priority'] as String,
    slaDueAt: j['slaDueAt'] == null ? null : DateTime.parse(j['slaDueAt'] as String),
    note: j['note'] as String?,
    faultCategory: j['faultCategory'] as String,
    faultDescription: j['faultDescription'] as String?,
    faultPowerAtInspection: j['faultPowerAtInspection'] as String,
    assignedAt: DateTime.parse(j['assignedAt'] as String),
    asset: AssetSummary.fromJson(j['asset'] as Map<String, dynamic>),
    progress: j['progress'] == null ? null : TaskProgress.fromJson(j['progress'] as Map<String, dynamic>),
    eventSeq: (j['eventSeq'] as int?) ?? 0,
  );

  final String id;
  final String code;
  final String state;
  final String priority;
  final DateTime? slaDueAt;
  final String? note;
  final String faultCategory;
  final String? faultDescription;
  final String faultPowerAtInspection;
  final DateTime assignedAt;
  final AssetSummary asset;

  /// What the server has already recorded for this work order (M3).
  final TaskProgress? progress;

  /// M4: the work order's version on the server (last event number) when the phone received it; sent back with each
  /// command as `baseVersion` so a change made meanwhile by someone else is refused, never overwritten.
  final int eventSeq;
}

/// Server-side progress of a work order (Mfop.WorkOrders.Contracts.TaskProgress).
class TaskProgress {
  TaskProgress({
    required this.checklistTemplateId,
    required this.diagnosisCode,
    required this.diagnosisNote,
    required this.arrivals,
    required this.beforePhotos,
    required this.afterPhotos,
    required this.testPhotos,
    required this.materials,
    required this.checklistRecorded,
    required this.testMethod,
    required this.submissions,
    required this.lastReturnReasons,
    required this.lastReturnNote,
  });

  factory TaskProgress.fromJson(Map<String, dynamic> j) => TaskProgress(
    checklistTemplateId: j['checklistTemplateId'] as String?,
    diagnosisCode: j['diagnosisCode'] as String?,
    diagnosisNote: j['diagnosisNote'] as String?,
    arrivals: j['arrivals'] as int? ?? 0,
    beforePhotos: j['beforePhotos'] as int? ?? 0,
    afterPhotos: j['afterPhotos'] as int? ?? 0,
    testPhotos: j['testPhotos'] as int? ?? 0,
    materials: j['materials'] as int? ?? 0,
    checklistRecorded: j['checklistRecorded'] as bool? ?? false,
    testMethod: j['testMethod'] as String?,
    submissions: j['submissions'] as int? ?? 0,
    lastReturnReasons: ((j['lastReturnReasons'] as List<dynamic>?) ?? const []).cast<String>(),
    lastReturnNote: j['lastReturnNote'] as String?,
  );

  final String? checklistTemplateId;
  final String? diagnosisCode;
  final String? diagnosisNote;
  final int arrivals;
  final int beforePhotos;
  final int afterPhotos;
  final int testPhotos;
  final int materials;
  final bool checklistRecorded;
  final String? testMethod;
  final int submissions;
  final List<String> lastReturnReasons;
  final String? lastReturnNote;
}

class DiagnosisCode {
  DiagnosisCode(this.code, this.nameAr, this.nameEn, this.requiresMaterial, this.powerSources, this.path);

  factory DiagnosisCode.fromJson(Map<String, dynamic> j) => DiagnosisCode(
    j['code'] as String,
    j['nameAr'] as String,
    j['nameEn'] as String,
    j['requiresMaterial'] as bool? ?? false,
    ((j['powerSources'] as List<dynamic>?) ?? const []).cast<String>(),
    j['path'] as String? ?? 'REPAIR',
  );

  final String code;
  final String nameAr;
  final String nameEn;
  final bool requiresMaterial;
  final List<String> powerSources;

  /// REPAIR codes are recorded in DEMO 1; NO_WORK codes (theft, grid off, …) arrive with ResolveNoWork (M6).
  final String path;
}

class ChecklistItem {
  ChecklistItem(this.code, this.textAr, this.textEn, this.expected, this.blocking, this.testItem);

  factory ChecklistItem.fromJson(Map<String, dynamic> j) => ChecklistItem(
    j['code'] as String,
    j['textAr'] as String,
    j['textEn'] as String,
    j['expected'] as String,
    j['blocking'] as bool? ?? false,
    j['testItem'] as bool? ?? false,
  );

  final String code;
  final String textAr;
  final String textEn;

  /// YES or NO.
  final String expected;
  final bool blocking;
  final bool testItem;
}

class TestMethod {
  TestMethod(this.code, this.textAr, this.textEn);

  factory TestMethod.fromJson(Map<String, dynamic> j) => TestMethod(j['code'] as String, j['textAr'] as String, j['textEn'] as String);

  final String code;
  final String textAr;
  final String textEn;
}

class EvidenceRequirements {
  const EvidenceRequirements({required this.beforeFirstArrival, required this.beforeLaterArrival, required this.after, required this.test});

  factory EvidenceRequirements.fromJson(Map<String, dynamic> j) => EvidenceRequirements(
    beforeFirstArrival: j['beforeFirstArrival'] as int? ?? 2,
    beforeLaterArrival: j['beforeLaterArrival'] as int? ?? 1,
    after: j['after'] as int? ?? 2,
    test: j['test'] as int? ?? 1,
  );

  final int beforeFirstArrival;
  final int beforeLaterArrival;
  final int after;
  final int test;
}

class ChecklistTemplate {
  ChecklistTemplate(this.id, this.code, this.version, this.items, this.testMethods, this.evidence);

  factory ChecklistTemplate.fromJson(Map<String, dynamic> j) => ChecklistTemplate(
    j['id'] as String,
    j['code'] as String,
    j['version'] as int? ?? 1,
    ((j['items'] as List<dynamic>?) ?? const []).map((e) => ChecklistItem.fromJson(e as Map<String, dynamic>)).toList(),
    ((j['testMethods'] as List<dynamic>?) ?? const []).map((e) => TestMethod.fromJson(e as Map<String, dynamic>)).toList(),
    EvidenceRequirements.fromJson((j['evidenceRequirements'] as Map<String, dynamic>?) ?? const {}),
  );

  final String id;
  final String code;
  final int version;
  final List<ChecklistItem> items;
  final List<TestMethod> testMethods;
  final EvidenceRequirements evidence;
}

/// Diagnosis codes and checklist templates, pulled and cached for offline use.
class FieldReference {
  FieldReference(this.diagnosisCodes, this.checklistTemplates);

  factory FieldReference.fromJson(Map<String, dynamic>? j) => FieldReference(
    ((j?['diagnosisCodes'] as List<dynamic>?) ?? const []).map((e) => DiagnosisCode.fromJson(e as Map<String, dynamic>)).toList(),
    ((j?['checklistTemplates'] as List<dynamic>?) ?? const []).map((e) => ChecklistTemplate.fromJson(e as Map<String, dynamic>)).toList(),
  );

  final List<DiagnosisCode> diagnosisCodes;
  final List<ChecklistTemplate> checklistTemplates;

  ChecklistTemplate? template(String? id) => checklistTemplates.where((t) => t.id == id).firstOrNull;

  /// Codes the technician may pick for this light: REPAIR path, matching its power source.
  List<DiagnosisCode> repairCodesFor(String powerSource) =>
      diagnosisCodes.where((d) => d.path == 'REPAIR' && d.powerSources.contains(powerSource)).toList();
}

/// One item in the technician's own custody (Mfop.Inventory.Contracts.CustodyItem).
class CustodyItem {
  CustodyItem(this.itemId, this.sku, this.nameAr, this.nameEn, this.unit, this.serialTracked, this.quantity, this.serials);

  factory CustodyItem.fromJson(Map<String, dynamic> j) => CustodyItem(
    j['itemId'] as String,
    j['sku'] as String,
    j['nameAr'] as String,
    j['nameEn'] as String,
    j['unit'] as String,
    j['serialTracked'] as bool? ?? false,
    (j['quantity'] as num).toDouble(),
    ((j['serials'] as List<dynamic>?) ?? const []).cast<String>(),
  );

  final String itemId;
  final String sku;
  final String nameAr;
  final String nameEn;
  final String unit;
  final bool serialTracked;
  final double quantity;
  final List<String> serials;
}

class CountedFailure {
  CountedFailure(this.workOrderCode, this.diagnosisCode, this.resolvedAt);

  factory CountedFailure.fromJson(Map<String, dynamic> j) =>
      CountedFailure(j['workOrderCode'] as String, j['diagnosisCode'] as String, DateTime.parse(j['resolvedAt'] as String));

  final String workOrderCode;
  final String diagnosisCode;
  final DateTime resolvedAt;
}

/// "Recurrent Failure — Full inspection recommended" with what the rule counted (docs/lean-pilot/11 §11.2 M3 item 10).
class Recommendation {
  Recommendation(this.code, this.count, this.threshold, this.windowDays, this.counted);

  factory Recommendation.fromJson(Map<String, dynamic> j) => Recommendation(
    j['code'] as String,
    j['count'] as int,
    j['threshold'] as int,
    j['windowDays'] as int,
    ((j['countedFailures'] as List<dynamic>?) ?? const []).map((e) => CountedFailure.fromJson(e as Map<String, dynamic>)).toList(),
  );

  final String code;
  final int count;
  final int threshold;
  final int windowDays;
  final List<CountedFailure> counted;
}

class AssetCardEvent {
  AssetCardEvent(this.at, this.action, this.actorName);

  factory AssetCardEvent.fromJson(Map<String, dynamic> j) =>
      AssetCardEvent(DateTime.parse(j['at'] as String), j['action'] as String, j['actorName'] as String);

  final DateTime at;
  final String action;
  final String actorName;
}

class AssetCard {
  AssetCard({required this.asset, required this.openFaults, required this.myOpenTasks, required this.recentEvents, this.recommendations = const []});

  factory AssetCard.fromJson(Map<String, dynamic> j) => AssetCard(
    asset: AssetSummary.fromJson(j['asset'] as Map<String, dynamic>),
    openFaults: j['openFaults'] as int,
    myOpenTasks: (j['myOpenTasks'] as List<dynamic>).map((e) => WorkOrderTask.fromJson(e as Map<String, dynamic>)).toList(),
    recentEvents: (j['recentEvents'] as List<dynamic>).map((e) => AssetCardEvent.fromJson(e as Map<String, dynamic>)).toList(),
    recommendations: ((j['recommendations'] as List<dynamic>?) ?? const []).map((e) => Recommendation.fromJson(e as Map<String, dynamic>)).toList(),
  );

  final List<Recommendation> recommendations;
  final AssetSummary asset;
  final int openFaults;
  final List<WorkOrderTask> myOpenTasks;
  final List<AssetCardEvent> recentEvents;
}

class PullResult {
  PullResult({required this.serverTime, required this.userName, required this.roles, required this.workOrders, required this.raw});

  factory PullResult.fromJson(Map<String, dynamic> j) {
    final user = j['user'] as Map<String, dynamic>;
    return PullResult(
      serverTime: DateTime.parse(j['serverTime'] as String),
      userName: user['displayName'] as String,
      roles: (user['roles'] as List<dynamic>).cast<String>(),
      workOrders: (j['workOrders'] as List<dynamic>).map((e) => WorkOrderTask.fromJson(e as Map<String, dynamic>)).toList(),
      raw: j,
    );
  }

  /// The response as received, cached locally so the phone shows the server's view while offline.
  final Map<String, dynamic> raw;

  final DateTime serverTime;
  final String userName;
  final List<String> roles;
  final List<WorkOrderTask> workOrders;
}

class PushResult {
  PushResult(this.commandId, this.result, this.code, this.message, this.entityId);

  factory PushResult.fromJson(Map<String, dynamic> j) =>
      PushResult(j['commandId'] as String, j['result'] as String, j['code'] as String?, j['message'] as String?, j['entityId'] as String?);

  final String commandId;

  /// APPLIED, DUPLICATE (already applied: success) or REJECTED.
  final String result;
  final String? code;
  final String? message;
  final String? entityId;
}
