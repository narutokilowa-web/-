using System.Text.Json;
using Mfop.Assets.Domain;

namespace Mfop.Assets.Contracts;

public sealed record RegistrationRequest(string Reason, string? ProjectRef, StreetLightAttributes Attributes);

public sealed record DecisionRequest(string? Note);

public sealed record RegistrationDto(
    Guid Id,
    Guid AssetId,
    string State,
    string Reason,
    string? ProjectRef,
    string CaptureChannel,
    Guid RegistrarId,
    string RegistrarName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    Guid? DecidedBy,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    int ReturnCount,
    StreetLightAttributes Attributes,
    string? AssetCode,
    IReadOnlyList<string> AllowedCommands);

public sealed record TagDto(string Code, string CheckCode, string HumanCode, string QrPayload);

public sealed record AssetVersionDto(int VersionNo, string SnapshotHash, Guid ApprovedBy, string ApprovedByName, DateTimeOffset ApprovedAt, string Reason);

public sealed record AssetDto(
    Guid Id,
    string? AssetCode,
    string LifecycleState,
    string? OperationalStatus,
    StreetLightAttributes Attributes,
    string CaptureChannel,
    TagDto? Tag,
    int CurrentVersionNo,
    IReadOnlyList<AssetVersionDto> Versions,
    Guid? RegistrationId);

public sealed record AssetListItem(Guid Id, string? AssetCode, string LifecycleState, string? OperationalStatus, string PowerSource, string? AddressText, decimal Latitude, decimal Longitude);

public sealed record HistoryEntry(
    long Seq,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string ActorName,
    IReadOnlyList<string> ActorRoles,
    string Action,
    string EntityType,
    string EntityId,
    JsonElement Changes,
    JsonElement? Facts,
    string? Reason,
    string Hash);
