using Mfop.Assets.Contracts;

namespace Mfop.WorkOrders.Contracts;

public sealed record CreateWorkOrderRequest(Guid FaultId, string Priority, string? Note);

public sealed record AssignRequest(Guid AssigneeId, string? Reason);

public sealed record CancelRequest(string? Reason);

public sealed record AssignmentDto(Guid Id, Guid AssigneeId, string AssigneeName, string State, DateTimeOffset StartedAt, string StartedByName, DateTimeOffset? EndedAt, string? EndReason);

public sealed record WorkOrderListItem(
    Guid Id, string Code, string State, string Priority, Guid AssetId, string? AssetCode, string? AssetAddress, string FaultCategory,
    Guid? AssigneeId, string? AssigneeName, DateTimeOffset CreatedAt, DateTimeOffset? SlaDueAt);

public sealed record WorkOrderDto(
    Guid Id,
    string Code,
    string State,
    string Priority,
    DateTimeOffset? SlaDueAt,
    string? Note,
    Guid FaultId,
    string FaultCategory,
    string FaultState,
    AssetSummary? Asset,
    Guid? AssigneeId,
    string? AssigneeName,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string? CancelReason,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<AssignmentDto> Assignments,
    IReadOnlyList<string> AllowedCommands);

/// <summary>A task on the technician's phone ("My tasks"), with the server's view of its progress.</summary>
/// <param name="EventSeq">Version of the work order the phone now holds (last wo_events.seq). The phone sends it back as
/// <c>baseVersion</c> with each command, so a change made meanwhile by someone else is detected, never overwritten.</param>
public sealed record WorkOrderTask(
    Guid Id, string Code, string State, string Priority, DateTimeOffset? SlaDueAt, string? Note, string FaultCategory,
    string? FaultDescription, string FaultPowerAtInspection, DateTimeOffset AssignedAt, AssetSummary Asset, TaskProgress? Progress = null,
    int EventSeq = 0);

/// <param name="LastReturnReasons">Reasons of the latest ReturnWorkOrder (shown at the top of a returned task).</param>
public sealed record TaskProgress(
    Guid? ChecklistTemplateId, string? DiagnosisCode, string? DiagnosisNote, int Arrivals, int BeforePhotos, int AfterPhotos, int TestPhotos,
    int Materials, bool ChecklistRecorded, string? TestMethod, int Submissions, IReadOnlyList<string> LastReturnReasons, string? LastReturnNote);

/// <summary>Reference data the phone needs offline (docs/lean-pilot/01 §1.4 pull): diagnosis codes and checklist templates.</summary>
public sealed record DiagnosisCodeDto(string Code, string NameAr, string NameEn, bool CountsAsFailure, bool RequiresMaterial,
    IReadOnlyList<string> ExpectedItems, IReadOnlyList<string> PowerSources, string Path);

public sealed record ChecklistItemDto(string Code, string TextAr, string TextEn, string Expected, bool Blocking, bool TestItem);

public sealed record TestMethodDto(string Code, string TextAr, string TextEn);

public sealed record EvidenceRequirementsDto(int BeforeFirstArrival, int BeforeLaterArrival, int After, int Test);

public sealed record ChecklistTemplateDto(Guid Id, string Code, int Version, IReadOnlyList<string> PowerSources, IReadOnlyList<ChecklistItemDto> Items,
    IReadOnlyList<TestMethodDto> TestMethods, EvidenceRequirementsDto EvidenceRequirements);

public sealed record FieldReferenceData(IReadOnlyList<DiagnosisCodeDto> DiagnosisCodes, IReadOnlyList<ChecklistTemplateDto> ChecklistTemplates);

public interface IWorkOrderReference
{
    Task<FieldReferenceData> ReferenceAsync(CancellationToken ct);
}

// ---- Technician commands (pushed through /sync/v1/push; payloads) ----

/// <param name="ScannedToken">What the camera read (QR URL or tag code). Either this or ManualCode + ManualReason.</param>
public sealed record ArriveOnSitePayload(Guid WorkOrderId, string? ScannedToken, string? ManualCode, string? ManualReason);

/// <param name="Kind">BEFORE, AFTER or TEST.</param>
public sealed record AddEvidencePayload(Guid WorkOrderId, Guid EvidenceId, string Kind, string Sha256, long SizeBytes, string? Mime, DateTimeOffset? CapturedAt,
    Guid? SupersedesEvidenceId, string? SupersedeReason);

public sealed record RecordDiagnosisPayload(Guid WorkOrderId, string Code, string? Note);

public sealed record StartWorkPayload(Guid WorkOrderId);

public sealed record ConsumeMaterialPayload(Guid WorkOrderId, Guid MovementId, Guid ItemId, decimal Quantity, IReadOnlyList<string>? Serials);

/// <param name="Answers">Item code → YES / NO.</param>
public sealed record RecordChecklistPayload(Guid WorkOrderId, Guid ChecklistId, Guid TemplateId, string TestMethod, string? NotTestedReason,
    IReadOnlyDictionary<string, string> Answers);

public sealed record SubmitWorkOrderPayload(Guid WorkOrderId, string? RootCauseNote, string? NoMaterialReason);

// ---- Supervisor review (web) ----

public sealed record ApproveRequest(string? Note);

public sealed record ReturnRequest(IReadOnlyList<string>? Reasons, string? Note);

public sealed record ArrivalDto(Guid Id, int ArrivalNo, Guid UserId, string UserName, bool TagScanned, string? ScannedCode, string? ManualCode, string? ManualReason,
    DateTimeOffset? DeviceTime, DateTimeOffset RecordedAt);

public sealed record ReviewEvidenceDto(Guid Id, string Kind, string State, string Sha256Device, string? Sha256Server, DateTimeOffset? CapturedAt,
    DateTimeOffset AnnouncedAt, DateTimeOffset? VerifiedAt, bool HasPreview, Guid? DuplicateOf, Guid? SupersededBy, string? SupersedeReason, string CapturedByName);

public sealed record ChecklistResultDto(Guid Id, Guid TemplateId, string TestMethod, string? NotTestedReason, string TestResult, bool Blocked,
    IReadOnlyDictionary<string, string> Answers, string RecordedByName, DateTimeOffset RecordedAt);

public sealed record SubmissionDto(Guid Id, int SubmissionNo, string SubmittedByName, DateTimeOffset SubmittedAt, string PayloadHash,
    IReadOnlyList<Guid> EvidenceIds, IReadOnlyList<Guid> MaterialIds, Guid? ChecklistId, string? RootCauseNote, string? NoMaterialReason,
    IReadOnlyList<Guid> AddedEvidenceIds, IReadOnlyList<Guid> AddedMaterialIds);

public sealed record ReviewDecisionDto(Guid Id, int SubmissionNo, string ReviewerName, string Decision, IReadOnlyList<string> Reasons, string? Note, DateTimeOffset DecidedAt);

public sealed record ReviewFlagDto(Guid Id, string RuleCode, string Severity, string State, System.Text.Json.JsonElement Explanation, DateTimeOffset CreatedAt);

/// <param name="PendingEvidence">Photos not yet VERIFIED (ANNOUNCED or UPLOADED): Approve stays disabled while this is &gt; 0.</param>
/// <param name="BlockedEvidence">Photos that can never be VERIFIED (HASH_MISMATCH / DUPLICATE) and are not superseded.</param>
public sealed record WorkOrderReviewDto(
    Guid WorkOrderId, string Code, string State, DiagnosisCodeDto? Diagnosis, string? DiagnosisNote, string? RootCauseNote, string? NoMaterialReason,
    string? TestMethod, string? TestResult, DateTimeOffset? FirstArrivedAt, DateTimeOffset? WorkStartedAt, DateTimeOffset? SubmittedAt,
    int? WorkMinutes, IReadOnlyList<ArrivalDto> Arrivals, IReadOnlyList<ReviewEvidenceDto> Evidence, IReadOnlyList<Mfop.Inventory.Contracts.MovementDto> Materials,
    IReadOnlyList<string> ExpectedItemsMissing, ChecklistTemplateDto? Template, ChecklistResultDto? Checklist, IReadOnlyList<SubmissionDto> Submissions,
    IReadOnlyList<ReviewDecisionDto> Reviews, IReadOnlyList<ReviewFlagDto> Flags, int PendingEvidence, int BlockedEvidence, bool CanApprove,
    string? ApproveBlockedReason, IReadOnlyList<string> Executors, string? SealHash, DateTimeOffset? ClosedAt, string? ClosedByName);

// ---- Dashboard (web, SUPERVISOR / AUDITOR) ----

public sealed record CountByKey(string Key, int Count);

public sealed record CompletedWorkOrder(Guid Id, string Code, Guid AssetId, string? AssetCode, string? AssetAddress, string? DiagnosisCode,
    DateTimeOffset ClosedAt, string ClosedByName, string? SealHash);

public sealed record DashboardSummary(
    IReadOnlyList<CountByKey> LightsByOperationalStatus, int OpenFaults, int OpenFaultsReportedWithoutPower,
    IReadOnlyList<CountByKey> OpenWorkOrdersByState, int PendingReview, IReadOnlyList<CompletedWorkOrder> RecentlyCompleted);

/// <summary>Read side for the Sync module (pull, tag card). Only the current assignee's non-terminal work orders.</summary>
public interface IWorkOrderTasks
{
    Task<IReadOnlyList<WorkOrderTask>> OpenTasksForAsync(Guid assigneeId, CancellationToken ct);
}


