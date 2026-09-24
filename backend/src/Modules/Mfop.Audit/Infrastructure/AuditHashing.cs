using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Mfop.Infrastructure.Auditing;
using Mfop.Kernel.Json;

namespace Mfop.Audit.Infrastructure;

/// <summary>
/// payload_hash = SHA-256(canonical JSON of the event without seq/prev_hash/hash)
/// hash         = SHA-256(prev_hash bytes || payload_hash bytes || seq as 8-byte big-endian)
/// Genesis prev_hash = 32 zero bytes. tools/audit-verify re-implements this independently.
/// </summary>
public static class AuditHashing
{
    public static readonly string GenesisHash = new('0', 64);

    public static JsonObject Payload(AuditEventDraft e) => new()
    {
        ["event_id"] = e.EventId.ToString("D"),
        ["occurred_at"] = AuditValues.FormatTimestamp(e.OccurredAt),
        ["actor_user_id"] = e.ActorUserId?.ToString("D"),
        ["actor_roles"] = new JsonArray([.. e.ActorRoles.Select(r => (JsonNode?)JsonValue.Create(r))]),
        ["device_id"] = e.DeviceId?.ToString("D"),
        ["action"] = e.Action,
        ["entity_type"] = e.EntityType,
        ["entity_id"] = e.EntityId,
        ["entity_version"] = e.EntityVersion,
        ["changes"] = e.Changes.DeepClone(),
        ["facts"] = e.Facts?.DeepClone(),
        ["reason"] = e.Reason,
        ["command_id"] = e.CommandId.ToString("D"),
        ["correlation_id"] = e.CorrelationId,
    };

    public static string PayloadHash(AuditEventDraft e) => CanonicalJson.Sha256Hex(Payload(e));

    public static string ChainHash(string prevHashHex, string payloadHashHex, long seq)
    {
        Span<byte> buffer = stackalloc byte[32 + 32 + 8];
        Convert.FromHexString(prevHashHex).CopyTo(buffer);
        Convert.FromHexString(payloadHashHex).CopyTo(buffer[32..]);
        BinaryPrimitives.WriteInt64BigEndian(buffer[64..], seq);
        return Convert.ToHexStringLower(SHA256.HashData(buffer));
    }
}
