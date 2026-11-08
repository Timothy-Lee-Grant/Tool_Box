namespace ToolBox.Embedded;

/// <summary>
/// The registration-time gate and confirm-token mechanism for `physical`-tier
/// tools (plan 006 §2.3). Two jobs: (1) <see cref="HardwareActionsAllowed"/>
/// decides whether <c>EmbeddedToolsetExtensions</c> registers `physical` tool
/// types at all — absent from the catalog when false, not merely refusing at
/// call time, per §2.3's "stronger than refusing at call time" argument; (2)
/// a short-lived, single-use confirm-token flow for the least-reversible
/// single action in this toolset (<c>flash_firmware</c>, plan 006 Step 5).
///
/// Takes <see cref="TimeProvider"/> instead of calling
/// <c>DateTimeOffset.UtcNow</c> directly, same reason as
/// <c>ServerInfoProvider</c> — tests control the clock, no
/// <c>Thread.Sleep</c>-based expiry tests.
/// </summary>
public sealed class PhysicalActionGate
{
    public const string EnvironmentVariableName = "TOOLBOX_ALLOW_HARDWARE_ACTIONS";

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(2);

    private readonly TimeProvider _clock;
    private readonly Dictionary<string, PendingToken> _pendingTokens = [];

    /// <summary>Production constructor — reads the real environment variable.</summary>
    public PhysicalActionGate(TimeProvider clock)
        : this(Environment.GetEnvironmentVariable(EnvironmentVariableName), clock)
    {
    }

    /// <summary>
    /// Test seam: pass the raw value directly rather than mutating process-wide
    /// environment state (which is also just unsafe under parallel test
    /// execution). Not resolvable from DI (no bare <see cref="string"/> is
    /// registered), so the container always picks the single-argument
    /// constructor above in production.
    /// </summary>
    public PhysicalActionGate(string? rawEnvironmentValue, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        HardwareActionsAllowed = IsTruthy(rawEnvironmentValue);
    }

    public bool HardwareActionsAllowed { get; }

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

    /// <summary>
    /// Issues a short-lived, single-use token naming exactly what physical
    /// action it authorizes. The token carries no meaning beyond "the agent
    /// was shown this summary and chose to proceed" — plan 006 §2.3's
    /// two-call confirm pattern (e.g. <c>request_flash</c> → <c>flash_firmware(token)</c>).
    /// </summary>
    public string IssueToken(string actionSummary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionSummary);

        string token = Guid.NewGuid().ToString("N");
        _pendingTokens[token] = new PendingToken(actionSummary, _clock.GetUtcNow() + TokenLifetime);
        return token;
    }

    /// <summary>
    /// Single-use: any token this recognizes is removed on this call whether
    /// it's still valid or already expired, so neither a stale token nor a
    /// valid one can be replayed.
    /// </summary>
    public PhysicalActionTokenStatus TryConsumeToken(string token, out string? actionSummary)
    {
        actionSummary = null;

        if (string.IsNullOrWhiteSpace(token) || !_pendingTokens.Remove(token, out PendingToken pending))
        {
            return PhysicalActionTokenStatus.Unknown;
        }

        if (pending.ExpiresAtUtc < _clock.GetUtcNow())
        {
            return PhysicalActionTokenStatus.Expired;
        }

        actionSummary = pending.ActionSummary;
        return PhysicalActionTokenStatus.Valid;
    }

    private readonly record struct PendingToken(string ActionSummary, DateTimeOffset ExpiresAtUtc);
}

public enum PhysicalActionTokenStatus
{
    Valid,
    Unknown,
    Expired,
}
