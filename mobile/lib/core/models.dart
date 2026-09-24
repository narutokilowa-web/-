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
  AssetCard({required this.asset, required this.openFaults, required this.myOpenTasks, required this.recentEvents});

  factory AssetCard.fromJson(Map<String, dynamic> j) => AssetCard(
    asset: AssetSummary.fromJson(j['asset'] as Map<String, dynamic>),
    openFaults: j['openFaults'] as int,
    myOpenTasks: (j['myOpenTasks'] as List<dynamic>).map((e) => WorkOrderTask.fromJson(e as Map<String, dynamic>)).toList(),
    recentEvents: (j['recentEvents'] as List<dynamic>).map((e) => AssetCardEvent.fromJson(e as Map<String, dynamic>)).toList(),
  );

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
