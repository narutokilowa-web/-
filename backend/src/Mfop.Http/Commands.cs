using Mfop.Kernel.Commands;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Http;

public static class HttpCommandExtensions
{
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>
    /// Builds the command envelope for a web request. If the client sends a UUID Idempotency-Key it becomes the
    /// command id recorded in the audit log. (Duplicate suppression is implemented with the sync pipeline in M2.)
    /// </summary>
    public static CommandEnvelope Command(this HttpContext http, string type, string? reason = null)
    {
        var actor = http.RequestServices.GetRequiredService<CurrentActor>().Required;
        Guid? commandId = http.Request.Headers.TryGetValue(IdempotencyHeader, out var key) && Guid.TryParse(key, out var parsed) ? parsed : null;
        return CommandEnvelope.For(type, actor, reason, commandId, http.TraceIdentifier);
    }
}
