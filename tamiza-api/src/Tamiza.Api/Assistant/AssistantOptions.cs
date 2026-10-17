using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Tamiza.Api.Assistant;

/// <summary>
/// Settings of the optional AI assistant, read from the flat <c>TAMIZA_ASSISTANT_*</c> variables. The assistant exists only
/// when the base URL, the model and the API key are all set (<see cref="Enabled"/>); Compose always passes optional strings,
/// often empty, so a blank value means absent. Text values are trimmed, so a key pasted with a trailing newline still works.
/// </summary>
public sealed class AssistantOptions
{
    public const string BaseUrlVariable = "TAMIZA_ASSISTANT_BASE_URL";
    public const string ModelVariable = "TAMIZA_ASSISTANT_MODEL";
    public const string ApiKeyVariable = "TAMIZA_ASSISTANT_API_KEY";
    public const string ExtraBodyVariable = "TAMIZA_ASSISTANT_EXTRA_BODY";
    public const string TimeoutSecondsVariable = "TAMIZA_ASSISTANT_TIMEOUT_SECONDS";
    public const string DailyTokensPerUserVariable = "TAMIZA_ASSISTANT_DAILY_TOKENS_PER_USER";
    public const string MaxContextKbVariable = "TAMIZA_ASSISTANT_MAX_CONTEXT_KB";

    public const int DefaultTimeoutSeconds = 120;
    public const int DefaultDailyTokensPerUser = 500_000;
    public const int DefaultMaxContextKb = 128;

    /// <summary>Fields the extra request fields may not set: the assistant owns them.</summary>
    public static readonly IReadOnlyList<string> ReservedBodyFields = ["model", "messages", "stream"];

    private string _baseUrl = "";
    private string _model = "";
    private string _apiKey = "";
    private string _extraBody = "";

    /// <summary>Absolute <c>http</c> or <c>https</c> URL of the provider's OpenAI-compatible API, such as <c>https://host/v1</c>.</summary>
    [ConfigurationKeyName(BaseUrlVariable)]
    public string BaseUrl { get => _baseUrl; set => _baseUrl = value?.Trim() ?? ""; }

    /// <summary>Sent as <c>model</c>.</summary>
    [ConfigurationKeyName(ModelVariable)]
    public string Model { get => _model; set => _model = value?.Trim() ?? ""; }

    /// <summary>Sent as <c>Authorization: Bearer</c>. Never logged, never returned.</summary>
    [ConfigurationKeyName(ApiKeyVariable)]
    public string ApiKey { get => _apiKey; set => _apiKey = value?.Trim() ?? ""; }

    /// <summary>A JSON object merged into every request body. It may not set <see cref="ReservedBodyFields"/>.</summary>
    [ConfigurationKeyName(ExtraBodyVariable)]
    public string ExtraBody { get => _extraBody; set => _extraBody = value?.Trim() ?? ""; }

    [ConfigurationKeyName(TimeoutSecondsVariable)]
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    [ConfigurationKeyName(DailyTokensPerUserVariable)]
    public int DailyTokensPerUser { get; set; } = DefaultDailyTokensPerUser;

    [ConfigurationKeyName(MaxContextKbVariable)]
    public int MaxContextKb { get; set; } = DefaultMaxContextKb;

    /// <summary>True when the base URL, the model and the key are all set.</summary>
    public bool Enabled => BaseUrl.Length > 0 && Model.Length > 0 && ApiKey.Length > 0;

    /// <summary>The names of the three variables that turn the assistant on and are not set.</summary>
    public IReadOnlyList<string> MissingVariables()
    {
        var missing = new List<string>();
        if (BaseUrl.Length == 0)
        {
            missing.Add(BaseUrlVariable);
        }

        if (Model.Length == 0)
        {
            missing.Add(ModelVariable);
        }

        if (ApiKey.Length == 0)
        {
            missing.Add(ApiKeyVariable);
        }

        return missing;
    }

    /// <summary>The base URL with a trailing <c>/</c>, so a relative path resolves under it; null when unset or malformed.</summary>
    public Uri? BaseUri => TryParseBaseUrl(BaseUrl, out var uri) ? uri : null;

    /// <summary>The provider's host name, for the disclosure line; never the path, the model or the key.</summary>
    public string? Host => BaseUri?.Host;

    /// <summary>The extra request fields as a JSON object; empty when none are set or the text is not a valid object.</summary>
    public JsonObject ExtraFields() => TryParseExtraBody(ExtraBody, out var fields, out _) ? fields : [];

    internal static bool TryParseBaseUrl(string value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(parsed.Host)
            || parsed.Query.Length > 0
            || parsed.Fragment.Length > 0
            || parsed.UserInfo.Length > 0)
        {
            return false;
        }

        uri = new Uri(parsed.AbsoluteUri.EndsWith('/') ? parsed.AbsoluteUri : parsed.AbsoluteUri + "/");
        return true;
    }

    internal static bool TryParseExtraBody(string value, out JsonObject fields, out string error)
    {
        fields = [];
        error = "";
        if (value.Length == 0)
        {
            return true;
        }

        try
        {
            if (JsonNode.Parse(value) is not JsonObject parsed)
            {
                error = "must be a JSON object, for example {\"reasoning_effort\": \"low\"}";
                return false;
            }

            var reserved = ReservedBodyFields.Where(parsed.ContainsKey).ToList();
            if (reserved.Count > 0)
            {
                error = $"may not set {string.Join(", ", reserved)}: the assistant sets them";
                return false;
            }

            fields = parsed;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            // The parser's message could quote the value, so it is not repeated.
            error = "must be a valid JSON object";
            return false;
        }
    }
}

/// <summary>
/// Stops the api at startup, naming the variable, for values that are typos the operator must see at deploy time. A missing
/// value is not an error: it turns the assistant off.
/// </summary>
public sealed class AssistantOptionsValidator : IValidateOptions<AssistantOptions>
{
    public ValidateOptionsResult Validate(string? name, AssistantOptions options)
    {
        var failures = new List<string>();

        if (options.BaseUrl.Length > 0 && !AssistantOptions.TryParseBaseUrl(options.BaseUrl, out _))
        {
            failures.Add($"{AssistantOptions.BaseUrlVariable} must be an absolute http or https URL without credentials, query or fragment, such as https://api.example.org/v1.");
        }

        if (!AssistantOptions.TryParseExtraBody(options.ExtraBody, out _, out var extraError))
        {
            failures.Add($"{AssistantOptions.ExtraBodyVariable} {extraError}.");
        }

        CheckRange(failures, AssistantOptions.TimeoutSecondsVariable, options.TimeoutSeconds, 10, 240);
        CheckRange(failures, AssistantOptions.DailyTokensPerUserVariable, options.DailyTokensPerUser, 1_000, 100_000_000);
        CheckRange(failures, AssistantOptions.MaxContextKbVariable, options.MaxContextKb, 64, 1_024);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckRange(List<string> failures, string variable, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            failures.Add($"{variable} must be between {min} and {max}, not {value}.");
        }
    }
}
