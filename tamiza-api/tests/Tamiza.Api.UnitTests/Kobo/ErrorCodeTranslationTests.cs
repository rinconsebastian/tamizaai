using System.Reflection;
using System.Text.Json;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.UnitTests.Kobo;

/// <summary>
/// The UI translates the codes the API returns and records (<c>kobo.*</c>). A code without a message would reach people as
/// raw text, so every <see cref="KoboErrorCodes"/> constant must have one in the UI's English file.
/// </summary>
public sealed class ErrorCodeTranslationTests
{
    private static JsonElement? ReadTranslations()
    {
        // The UI sits next to the API in the repository; a build that ships only the API has nothing to compare with.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "tamiza-ui", "public", "i18n", "en.json");
            if (File.Exists(path))
            {
                return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
            }
        }

        return null;
    }

    private static IEnumerable<string> AllCodes() =>
        typeof(KoboErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    public static TheoryData<string> Codes() => new(AllCodes());

    [Theory]
    [MemberData(nameof(Codes))]
    public void Every_kobo_error_code_has_a_message_in_the_ui(string code)
    {
        if (ReadTranslations() is not { } translations)
        {
            return;
        }

        // "kobo.unreachable" is errors.codes.kobo.unreachable in the file.
        var node = translations.GetProperty("errors").GetProperty("codes");
        foreach (var part in code.Split('.'))
        {
            Assert.True(node.TryGetProperty(part, out node), $"The UI has no message for the API error code '{code}'.");
        }

        Assert.Equal(JsonValueKind.String, node.ValueKind);
    }

    [Fact]
    public void The_codes_to_check_include_the_version_and_rate_limit_codes()
    {
        Assert.Contains("kobo.version_not_found", AllCodes());
        Assert.Contains("kobo.rate_limited", AllCodes());
    }
}
