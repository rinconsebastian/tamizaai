using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Tamiza.Api.Assistant;

/// <summary>One message of the conversation sent to the provider; <paramref name="Role"/> is <c>system</c>, <c>user</c> or <c>assistant</c>.</summary>
public sealed record AssistantMessage(string Role, string Content);

/// <summary>The token counts a provider reported. Any of them may be absent.</summary>
public sealed record AssistantUsage(int? PromptTokens, int? CompletionTokens, int? TotalTokens)
{
    /// <summary>
    /// What the call cost: the larger of <c>total_tokens</c> and the sum of the other two, because some providers count
    /// thinking tokens only in the total. Null when the provider reported none.
    /// </summary>
    public int? Tokens
    {
        get
        {
            if (PromptTokens is null && CompletionTokens is null && TotalTokens is null)
            {
                return null;
            }

            var sum = (long)(PromptTokens ?? 0) + (CompletionTokens ?? 0);
            return (int)Math.Min(int.MaxValue, Math.Max(TotalTokens ?? 0, sum));
        }
    }
}

/// <summary>A usable answer. <paramref name="Truncated"/> is true when the provider stopped at its output limit (<c>finish_reason</c> <c>length</c>).</summary>
public sealed record AssistantAnswer(
    string Text, bool Truncated, string? FinishReason, int? PromptTokens, int? CompletionTokens, int? TotalTokens)
{
    public AssistantUsage Usage => new(PromptTokens, CompletionTokens, TotalTokens);

    /// <summary>See <see cref="AssistantUsage.Tokens"/>.</summary>
    public int? Tokens => Usage.Tokens;
}

public enum AssistantFailureKind
{
    /// <summary>Unreachable, a redirect, a server error, an unusable body or an answer without text (<c>assistant.unavailable</c>, 502).</summary>
    Unavailable,

    /// <summary>The provider refused the key, the model or a field (<c>assistant.misconfigured</c>, 503).</summary>
    Misconfigured,

    /// <summary>HTTP 429 (<c>assistant.rateLimited</c>, 429).</summary>
    RateLimited,

    /// <summary>The call outlasted the client's timeout (<c>assistant.timeout</c>, 504).</summary>
    Timeout,

    /// <summary>The caller cancelled; <see cref="AssistantFailure.Code"/> is null and nothing is shown to anyone.</summary>
    Cancelled,
}

/// <summary>
/// Why a call gave no answer. <paramref name="Code"/> is one of <see cref="AssistantErrorCodes"/> (null for a cancellation),
/// <paramref name="RetryAfterSeconds"/> is the provider's <c>Retry-After</c> for a rate limit, and
/// <paramref name="ProviderMayHaveWorked"/> is true when the provider may have spent tokens (a 2xx answer that was unusable,
/// a timeout, a cancellation): the only failures whose cost is recorded as an estimate. <paramref name="Usage"/> holds the
/// counts a 2xx answer reported even though it was unusable.
/// </summary>
public sealed record AssistantFailure(
    AssistantFailureKind Kind, string? Code, int? RetryAfterSeconds, bool ProviderMayHaveWorked, AssistantUsage? Usage = null);

/// <summary>
/// The outcome of one provider call: exactly one of <see cref="Answer"/> and <see cref="Failure"/>.
/// <see cref="SentBytes"/> is the UTF-8 size of the messages sent and <see cref="ReceivedBytes"/> that of the answer text
/// received (0 when none), from which <see cref="EstimatedTokens"/> follows when the provider reported no usage.
/// </summary>
public sealed record AssistantResult(AssistantAnswer? Answer, AssistantFailure? Failure, long SentBytes, long ReceivedBytes)
{
    public bool Succeeded => Answer is not null;

    /// <summary>The tokens the provider reported for this call, or null.</summary>
    public int? ReportedTokens => Answer?.Tokens ?? Failure?.Usage?.Tokens;

    /// <summary>UTF-8 bytes of what was sent and received, divided by four and rounded up.</summary>
    public int EstimatedTokens => (int)Math.Min(int.MaxValue, (SentBytes + ReceivedBytes + 3) / 4);
}

/// <summary>
/// Calls an OpenAI-compatible <c>chat/completions</c> endpoint. The key goes only in the <c>Authorization</c> header; response
/// and error bodies are read to extract the answer and are never logged nor relayed. It sends no field the provider might
/// reject (no <c>temperature</c>, <c>max_tokens</c>, <c>stream</c>, tools...): an operator who needs one sets it in
/// <c>TAMIZA_ASSISTANT_EXTRA_BODY</c>.
/// </summary>
public sealed class AssistantClient(
    IHttpClientFactory httpClientFactory, IOptions<AssistantOptions> options, TimeProvider time, ILogger<AssistantClient> logger)
{
    private static readonly TimeSpan LogInterval = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, DateTimeOffset> _lastLogged = [];

    /// <summary>
    /// Sends the conversation, system message first, and returns the answer or the failure. Never throws for a provider
    /// problem; throws <see cref="InvalidOperationException"/> when the assistant is not configured.
    /// </summary>
    public async Task<AssistantResult> CompleteAsync(IReadOnlyList<AssistantMessage> messages, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || settings.BaseUri is not { } baseUri)
        {
            throw new InvalidOperationException("The AI assistant is not configured.");
        }

        var sent = messages.Sum(m => (long)Encoding.UTF8.GetByteCount(m.Content));
        AssistantResult Fail(AssistantFailure failure, long received = 0) => new(null, failure, sent, received);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "chat/completions"))
        {
            Content = new StringContent(BuildBody(settings, messages), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(AssistantHttp.ClientName).SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail(new AssistantFailure(AssistantFailureKind.Cancelled, null, null, ProviderMayHaveWorked: true));
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("The AI assistant provider at {Host} did not answer within the timeout.", settings.Host);
            return Fail(new AssistantFailure(AssistantFailureKind.Timeout, AssistantErrorCodes.Timeout, null, ProviderMayHaveWorked: true));
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
        {
            // The body is buffered up to the client's limit, so this was an answer that was too large, not a connection problem.
            logger.LogWarning("The AI assistant provider at {Host} sent a response larger than the limit.", settings.Host);
            return Fail(Unavailable(mayHaveWorked: true));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("The AI assistant provider at {Host} could not be reached: {Reason}.", settings.Host, exception.HttpRequestError);
            return Fail(Unavailable(mayHaveWorked: false));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Fail(FailureOf(response, settings));
            }

            return await ReadAnswerAsync(response, settings, sent, cancellationToken);
        }
    }

    private static string BuildBody(AssistantOptions settings, IReadOnlyList<AssistantMessage> messages)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = new JsonArray(messages
                .Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content })
                .ToArray()),
        };
        foreach (var (name, value) in settings.ExtraFields())
        {
            if (!AssistantOptions.ReservedBodyFields.Contains(name))
            {
                body[name] = value?.DeepClone();
            }
        }

        return body.ToJsonString();
    }

    private AssistantFailure FailureOf(HttpResponseMessage response, AssistantOptions settings)
    {
        var status = (int)response.StatusCode;
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.PaymentRequired or HttpStatusCode.Forbidden or HttpStatusCode.NotFound:
                // One category per status, so a 402 (out of credit) is not hidden by a 401 logged a moment earlier.
                if (ShouldLog($"misconfigured:{status}"))
                {
                    logger.LogError("The AI assistant provider at {Host} answered {Status}: check the key, the model and the base URL.", settings.Host, status);
                }

                return new AssistantFailure(AssistantFailureKind.Misconfigured, AssistantErrorCodes.Misconfigured, null, false);
            case HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity:
                if (ShouldLog($"misconfigured:{status}"))
                {
                    logger.LogError("The AI assistant provider at {Host} answered {Status}: it rejected a request field; check the model and TAMIZA_ASSISTANT_EXTRA_BODY.", settings.Host, status);
                }

                return new AssistantFailure(AssistantFailureKind.Misconfigured, AssistantErrorCodes.Misconfigured, null, false);
            case HttpStatusCode.TooManyRequests:
                if (ShouldLog("rateLimited"))
                {
                    logger.LogWarning("The AI assistant provider at {Host} answered 429: it limits the requests or the credit is spent.", settings.Host);
                }

                return new AssistantFailure(AssistantFailureKind.RateLimited, AssistantErrorCodes.RateLimited, RetryAfterSeconds(response), false);
            default:
                // 3xx (never followed), 5xx, 408, 409, 413, 499 and anything else unexpected.
                logger.LogWarning("The AI assistant provider at {Host} answered {Status}.", settings.Host, status);
                return Unavailable(mayHaveWorked: false);
        }
    }

    private int? RetryAfterSeconds(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        double? seconds = null;
        if (retry?.Delta is { } delta)
        {
            seconds = delta.TotalSeconds;
        }
        else if (retry?.Date is { } date)
        {
            seconds = (date - time.GetUtcNow()).TotalSeconds;
        }

        return seconds is { } value ? (int)Math.Clamp(Math.Ceiling(value), 1, 86_400) : null;
    }

    private async Task<AssistantResult> ReadAnswerAsync(HttpResponseMessage response, AssistantOptions settings, long sent, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AssistantResult(null, new AssistantFailure(AssistantFailureKind.Cancelled, null, null, true), sent, 0);
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or IOException)
        {
            logger.LogWarning("The AI assistant provider at {Host} sent a body that could not be read.", settings.Host);
            return new AssistantResult(null, Unavailable(mayHaveWorked: true), sent, 0);
        }

        using (document)
        {
            var root = document.RootElement;
            var choice = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].ValueKind == JsonValueKind.Object
                    ? choices[0]
                    : (JsonElement?)null;

            var text = choice is { } c && c.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("content", out var content)
                    ? TextOf(content)
                    : "";
            var finishReason = choice is { } f && f.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String
                ? reason.GetString()
                : null;
            var usage = UsageOf(root);
            var received = Encoding.UTF8.GetByteCount(text);

            AssistantResult Unusable(string why)
            {
                logger.LogWarning("The AI assistant provider at {Host} gave no usable answer: {Why}.", settings.Host, why);
                return new AssistantResult(null, Unavailable(mayHaveWorked: true, usage), sent, received);
            }

            if (string.Equals(finishReason, "error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(finishReason, "content_filter", StringComparison.OrdinalIgnoreCase))
            {
                return Unusable($"finish_reason {finishReason!.ToLowerInvariant()}");
            }

            var truncated = string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Unusable(truncated
                    ? "the output limit was reached before any text (a reasoning model may have used all of it)"
                    : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out _)
                        ? "the body holds an error"
                        : "the answer has no text");
            }

            return new AssistantResult(
                new AssistantAnswer(text, truncated, finishReason, usage?.PromptTokens, usage?.CompletionTokens, usage?.TotalTokens),
                null, sent, received);
        }
    }

    /// <summary>The message content: a string, or the concatenated <c>text</c> parts of an array.</summary>
    private static string TextOf(JsonElement content)
    {
        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                return content.GetString() ?? "";
            case JsonValueKind.Array:
                var builder = new StringBuilder();
                foreach (var part in content.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(part.GetString());
                    }
                    else if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(text.GetString());
                    }
                }

                return builder.ToString();
            default:
                return "";
        }
    }

    private static AssistantUsage? UsageOf(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        int? Count(string name) =>
            usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0
                ? (int)Math.Min(number, int.MaxValue)
                : null;

        var result = new AssistantUsage(Count("prompt_tokens"), Count("completion_tokens"), Count("total_tokens"));
        return result.Tokens is null ? null : result;
    }

    private static AssistantFailure Unavailable(bool mayHaveWorked, AssistantUsage? usage = null) =>
        new(AssistantFailureKind.Unavailable, AssistantErrorCodes.Unavailable, null, mayHaveWorked, usage);

    /// <summary>True at most once a minute per category, so a provider that is down does not flood the log.</summary>
    private bool ShouldLog(string category)
    {
        var now = time.GetUtcNow();
        lock (_lastLogged)
        {
            if (_lastLogged.TryGetValue(category, out var last) && now - last < LogInterval)
            {
                return false;
            }

            _lastLogged[category] = now;
            return true;
        }
    }
}
