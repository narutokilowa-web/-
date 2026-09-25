using Mfop.Kernel.Results;
using Microsoft.AspNetCore.Http;

namespace Mfop.Http;

/// <summary>RFC 9457 problem responses carrying a stable business "code".</summary>
public static class Problems
{
    public static IResult Forbidden(string code, string detail) => Create(StatusCodes.Status403Forbidden, code, detail, null);

    public static IResult From(Error error) => Create(StatusOf(error.Kind), error.Code, error.Message, error.Details);

    public static int StatusOf(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.SodViolation => StatusCodes.Status403Forbidden,
        ErrorKind.InvalidTransition => StatusCodes.Status409Conflict,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Create(int status, string code, string detail, IReadOnlyDictionary<string, string>? details)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (details is not null)
        {
            extensions["details"] = details;
        }

        return Results.Problem(statusCode: status, title: code, detail: detail, extensions: extensions);
    }
}

public static class ResultHttpExtensions
{
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) =>
        result.IsSuccess ? (onSuccess ?? (v => Results.Ok(v)))(result.Value) : Problems.From(result.Error!);
}
