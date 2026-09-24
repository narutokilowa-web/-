namespace Mfop.Kernel.Results;

public enum ErrorKind
{
    Validation,
    NotFound,
    Forbidden,
    SodViolation,
    InvalidTransition,
    Conflict,
}

/// <summary>
/// A business error with a stable, machine-readable <see cref="Code"/> (e.g. SOD_VIOLATION).
/// Codes are part of the API contract: clients and tests depend on them.
/// </summary>
public sealed record Error(string Code, string Message, ErrorKind Kind, IReadOnlyDictionary<string, string>? Details = null)
{
    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string>? details = null) =>
        new(code, message, ErrorKind.Validation, details);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error Sod(string message) => new(ErrorCodes.SodViolation, message, ErrorKind.SodViolation);

    public static Error InvalidTransition(string machine, string state, string command) =>
        new(ErrorCodes.InvalidTransition, $"{machine}: command {command} is not allowed in state {state}.", ErrorKind.InvalidTransition,
            new Dictionary<string, string> { ["machine"] = machine, ["state"] = state, ["command"] = command });

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
}

/// <summary>Stable error codes shared across modules.</summary>
public static class ErrorCodes
{
    public const string SodViolation = "SOD_VIOLATION";
    public const string InvalidTransition = "INVALID_TRANSITION";
    public const string Forbidden = "FORBIDDEN";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string ValidationFailed = "VALIDATION_FAILED";
}
