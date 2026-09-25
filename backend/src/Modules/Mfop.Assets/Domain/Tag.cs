using Mfop.Kernel.Auditing;

namespace Mfop.Assets.Domain;

/// <summary>
/// QR tag identity. In DEMO 1 the tag is generated when the asset is approved; pre-printed tag batches issued
/// as controlled stock arrive in M7 (same code format and resolution path).
/// </summary>
public sealed class Tag : IAuditable
{
    private Tag() { }

    public Tag(Guid id, string code, string checkCode, Guid assetId, DateTimeOffset boundAt)
    {
        Id = id;
        Code = code;
        CheckCode = checkCode;
        Status = "BOUND";
        BoundAssetId = assetId;
        BoundAt = boundAt;
        Origin = "GENERATED_ON_APPROVAL";
    }

    public string AuditEntityType => "tag";

    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public string CheckCode { get; private set; } = "";
    public string Status { get; private set; } = "BOUND";
    public string Origin { get; private set; } = "GENERATED_ON_APPROVAL";
    public Guid? BoundAssetId { get; private set; }
    public DateTimeOffset? BoundAt { get; private set; }
    public int Version { get; private set; }

    /// <summary>Human-readable form printed under the QR code, e.g. 7K3M92QX.4F.</summary>
    public string HumanCode => $"{Code}.{CheckCode}";
}
