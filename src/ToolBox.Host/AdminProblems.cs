using Microsoft.AspNetCore.Http;

namespace ToolBox.Host;

/// <summary>
/// RFC 9457 <c>application/problem+json</c> errors for the admin API. Each carries a stable
/// <c>code</c> extension: the GUI switches on the code, never on the prose.
/// </summary>
internal static class AdminProblems
{
    public static IResult Create(int status, string code, string title, IDictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (extra is not null)
            foreach (var (k, v) in extra) extensions[k] = v;
        return Results.Problem(statusCode: status, title: title, extensions: extensions);
    }

    public static IResult UnknownToolset(string id) =>
        Create(StatusCodes.Status404NotFound, "unknown-toolset", $"No toolset with id '{id}'.");

    public static IResult PinnedByEnv(string pinnedBy) =>
        Create(StatusCodes.Status409Conflict, "pinned-by-env",
            "This toolset's enabled flag is pinned by the host's environment; nothing was written.",
            new Dictionary<string, object?> { ["pinnedBy"] = pinnedBy });

    public static IResult UnsupportedFile(int? found, int supported) =>
        Create(StatusCodes.Status409Conflict, "desired-state-unsupported",
            "The desired-state file was written by a newer build; nothing was written.",
            new Dictionary<string, object?> { ["foundVersion"] = found, ["supportedVersion"] = supported });

    public static IResult WriteFailed(string? reason) =>
        Create(StatusCodes.Status500InternalServerError, "desired-state-write-failed",
            "The desired-state file could not be written; the previous file is intact.",
            new Dictionary<string, object?> { ["reason"] = reason });

    public static IResult InvalidBody(string detail) =>
        Create(StatusCodes.Status400BadRequest, "invalid-body", detail);
}
