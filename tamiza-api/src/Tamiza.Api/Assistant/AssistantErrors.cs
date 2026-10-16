namespace Tamiza.Api.Assistant;

/// <summary>Machine-readable codes returned in the <c>code</c> extension of assistant Problem Details (design D10).</summary>
public static class AssistantErrorCodes
{
    /// <summary>The deployment has not configured the assistant (503).</summary>
    public const string Disabled = "assistant.disabled";

    /// <summary>The provider refused the key, the model or a request field (503).</summary>
    public const string Misconfigured = "assistant.misconfigured";

    /// <summary>The provider could not be reached or gave no usable answer (502).</summary>
    public const string Unavailable = "assistant.unavailable";

    /// <summary>The provider did not answer in time (504).</summary>
    public const string Timeout = "assistant.timeout";

    /// <summary>The provider limits the requests; the Problem carries <c>retryAfter</c> (429).</summary>
    public const string RateLimited = "assistant.rateLimited";

    /// <summary>The user's daily token budget is spent; the Problem carries <c>renewsAt</c> (429).</summary>
    public const string Budget = "assistant.budget";

    /// <summary>The user already has a request in progress (409).</summary>
    public const string Busy = "assistant.busy";

    /// <summary>The test a fix refers to does not match the request (409).</summary>
    public const string TestStale = "assistant.testStale";

    /// <summary>The draft or the assembled context is too large for the assistant (422).</summary>
    public const string ContextTooLarge = "assistant.contextTooLarge";

    /// <summary>Stored on a call that was still running when the api restarted. Never sent to a client.</summary>
    public const string Interrupted = "assistant.interrupted";
}
