using Mfop.Kernel.Auditing;

namespace Mfop.WorkOrders.Domain;

/// <summary>One event in the work order's own history (append-only; part of the seal). Data values are strings, booleans or null.</summary>
public sealed class WorkOrderEvent : IAuditable
{
    private WorkOrderEvent() { }

    public WorkOrderEvent(Guid id, Guid workOrderId, int seq, string type, Guid? actorId, Guid? deviceId, Guid commandId, string data,
        DateTimeOffset? deviceTime, DateTimeOffset recordedAt)
    {
        Id = id;
        WorkOrderId = workOrderId;
        Seq = seq;
        Type = type;
        ActorId = actorId;
        DeviceId = deviceId;
        CommandId = commandId;
        Data = data;
        DeviceTime = deviceTime;
        RecordedAt = recordedAt;
    }

    public string AuditEntityType => "work_order_event";

    public Guid Id { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public int Seq { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public Guid? ActorId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public Guid CommandId { get; private set; }

    /// <summary>JSON object.</summary>
    public string Data { get; private set; } = "{}";

    /// <summary>Phone clock at capture, as reported (trusted time arrives in M4).</summary>
    public DateTimeOffset? DeviceTime { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}

/// <summary>An arrival on site (W5): scanned tag or typed code with a reason (append-only).</summary>
public sealed class WorkOrderArrival : IAuditable
{
    private WorkOrderArrival() { }

    public WorkOrderArrival(Guid id, Guid workOrderId, Guid userId, int arrivalNo, bool tagScanned, string? scannedCode, string? manualCode,
        string? manualReason, DateTimeOffset? deviceTime, DateTimeOffset recordedAt)
    {
        Id = id;
        WorkOrderId = workOrderId;
        UserId = userId;
        ArrivalNo = arrivalNo;
        TagScanned = tagScanned;
        ScannedCode = scannedCode;
        ManualCode = manualCode;
        ManualReason = manualReason;
        DeviceTime = deviceTime;
        RecordedAt = recordedAt;
    }

    public string AuditEntityType => "work_order_arrival";

    public Guid Id { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public Guid UserId { get; private set; }
    public int ArrivalNo { get; private set; }
    public bool TagScanned { get; private set; }
    public string? ScannedCode { get; private set; }
    public string? ManualCode { get; private set; }
    public string? ManualReason { get; private set; }
    public DateTimeOffset? DeviceTime { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}

public static class DiagnosisPaths
{
    /// <summary>Repair path (StartWork → Submit). The NO_WORK path (ResolveNoWork, W7) is deferred to M6.</summary>
    public const string Repair = "REPAIR", NoWork = "NO_WORK";
}

/// <summary>Seeded reference data (02 §2.2), editable only through a seed change, never a UI.</summary>
public sealed class DiagnosisCode : IAuditable
{
    private DiagnosisCode() { }

    public DiagnosisCode(Guid id, string code, string nameAr, string nameEn, bool countsAsFailure, bool requiresMaterial, string[] expectedItems,
        string[] powerSources, string path)
    {
        Id = id;
        Code = code;
        NameAr = nameAr;
        NameEn = nameEn;
        CountsAsFailure = countsAsFailure;
        RequiresMaterial = requiresMaterial;
        ExpectedItems = expectedItems;
        PowerSources = powerSources;
        Path = path;
    }

    public string AuditEntityType => "diagnosis_code";

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>Counted by the repeated-failure rule. GRID_SUPPLY_OFF, THEFT, VANDALISM, NO_FAULT_FOUND and ASSET_* are false.</summary>
    public bool CountsAsFailure { get; private set; }
    public bool RequiresMaterial { get; private set; }

    /// <summary>Item SKUs the diagnosis usually needs (suggested on the phone).</summary>
    public string[] ExpectedItems { get; private set; } = [];

    /// <summary>Power sources the code applies to (GRID, SOLAR, HYBRID): the phone filters by the light's power source.</summary>
    public string[] PowerSources { get; private set; } = [];
    public string Path { get; private set; } = DiagnosisPaths.Repair;
    public int Version { get; private set; }
}

/// <summary>A published checklist template version, never edited in place (items, test methods and evidence requirements are data).</summary>
public sealed class ChecklistTemplate : IAuditable
{
    private ChecklistTemplate() { }

    public ChecklistTemplate(Guid id, string code, int templateVersion, string[] powerSources, string items, string testMethods,
        string evidenceRequirements, DateTimeOffset publishedAt)
    {
        Id = id;
        Code = code;
        TemplateVersion = templateVersion;
        PowerSources = powerSources;
        Items = items;
        TestMethods = testMethods;
        EvidenceRequirements = evidenceRequirements;
        PublishedAt = publishedAt;
    }

    public string AuditEntityType => "checklist_template";

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public int TemplateVersion { get; private set; }
    public string[] PowerSources { get; private set; } = [];

    /// <summary>JSON array of {code, text_ar, text_en, expected (YES/NO), blocking, test_item}.</summary>
    public string Items { get; private set; } = "[]";

    /// <summary>JSON array of {code, text_ar, text_en}; NOT_TESTED requires a reason.</summary>
    public string TestMethods { get; private set; } = "[]";

    /// <summary>JSON object: before_first_arrival, before_later_arrival, after, test (minimum photo counts).</summary>
    public string EvidenceRequirements { get; private set; } = "{}";
    public DateTimeOffset PublishedAt { get; private set; }
}

/// <summary>A recorded checklist (append-only; the latest one counts for submission).</summary>
public sealed class ChecklistResult : IAuditable
{
    private ChecklistResult() { }

    public ChecklistResult(Guid id, Guid workOrderId, Guid templateId, string testMethod, string? notTestedReason, string answers, string testResult,
        bool blocked, Guid recordedBy, DateTimeOffset recordedAt)
    {
        Id = id;
        WorkOrderId = workOrderId;
        TemplateId = templateId;
        TestMethod = testMethod;
        NotTestedReason = notTestedReason;
        Answers = answers;
        TestResult = testResult;
        Blocked = blocked;
        RecordedBy = recordedBy;
        RecordedAt = recordedAt;
    }

    public string AuditEntityType => "checklist_result";

    public Guid Id { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public Guid TemplateId { get; private set; }
    public string TestMethod { get; private set; } = string.Empty;
    public string? NotTestedReason { get; private set; }

    /// <summary>JSON object item code → YES / NO.</summary>
    public string Answers { get; private set; } = "{}";

    /// <summary>PASS, FAIL or NOT_TESTED.</summary>
    public string TestResult { get; private set; } = string.Empty;

    /// <summary>A blocking item was answered differently from what "repaired" requires (e.g. exposed wires remain).</summary>
    public bool Blocked { get; private set; }
    public Guid RecordedBy { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}

/// <summary>A submission for review (W9 / W12), append-only, with the hash of its report.</summary>
public sealed class WorkOrderSubmission : IAuditable
{
    private WorkOrderSubmission() { }

    public WorkOrderSubmission(Guid id, Guid workOrderId, int submissionNo, Guid submittedBy, string report, string payloadHash, DateTimeOffset submittedAt)
    {
        Id = id;
        WorkOrderId = workOrderId;
        SubmissionNo = submissionNo;
        SubmittedBy = submittedBy;
        Report = report;
        PayloadHash = payloadHash;
        SubmittedAt = submittedAt;
    }

    public string AuditEntityType => "work_order_submission";

    public Guid Id { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public int SubmissionNo { get; private set; }
    public Guid SubmittedBy { get; private set; }

    /// <summary>JSON: what the technician submitted (evidence, materials, checklist, notes).</summary>
    public string Report { get; private set; } = "{}";
    public string PayloadHash { get; private set; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; private set; }
}

/// <summary>A supervisor decision on a submission (W10 APPROVE / W11 RETURN), append-only.</summary>
public sealed class WorkOrderReview : IAuditable
{
    private WorkOrderReview() { }

    public WorkOrderReview(Guid id, Guid workOrderId, Guid submissionId, Guid reviewerId, string decision, string[] reasons, string? note, DateTimeOffset decidedAt)
    {
        Id = id;
        WorkOrderId = workOrderId;
        SubmissionId = submissionId;
        ReviewerId = reviewerId;
        Decision = decision;
        Reasons = reasons;
        Note = note;
        DecidedAt = decidedAt;
    }

    public string AuditEntityType => "work_order_review";

    public Guid Id { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid ReviewerId { get; private set; }

    /// <summary>APPROVE or RETURN.</summary>
    public string Decision { get; private set; } = string.Empty;
    public string[] Reasons { get; private set; } = [];
    public string? Note { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }
}

public static class ReturnReasons
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "MISSING_EVIDENCE", "UNCLEAR_EVIDENCE", "CHECKLIST_INCOMPLETE", "MATERIAL_QUESTION", "DIAGNOSIS_QUESTION", "OTHER",
    };
}

public static class TestMethods
{
    public const string NotTested = "NOT_TESTED";
}
