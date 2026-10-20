using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Tamiza.Api.Pipeline;

/// <summary>
/// The naming rules of the analytics worker (<c>tamiza.names</c>), for names the API creates: reference table columns.
/// Both implementations are checked against the same cases (<c>tamiza-analytics/tests/fixtures/column_names.json</c>).
/// </summary>
public static partial class ColumnNames
{
    /// <summary>PostgreSQL's identifier limit; reduced names are ASCII, so it is also the length in characters.</summary>
    public const int MaxLength = 63;

    private const int HashLength = 6;
    private const string Fallback = "column";

    /// <summary>
    /// A name made of lowercase ASCII letters, digits and underscores: accents are dropped (<c>años</c> → <c>anos</c>),
    /// other characters become underscores, a leading digit gets <c>c_</c>, and a name over 63 characters is cut and
    /// given a short hash of the original so different long names stay different.
    /// </summary>
    public static string Reduce(string name)
    {
        var ascii = new StringBuilder(name.Length);
        foreach (var character in name.Normalize(NormalizationForm.FormKD))
        {
            if (character < 128)
            {
                ascii.Append(char.ToLowerInvariant(character));
            }
        }

        // Trailing underscores (from "Income (COP)") are dropped; leading ones are kept for system columns like "_id".
        var reduced = RepeatedUnderscores().Replace(Invalid().Replace(ascii.ToString(), "_"), "_").TrimEnd('_');
        if (reduced.Trim('_').Length == 0)
        {
            reduced = Fallback;
        }

        if (char.IsAsciiDigit(reduced[0]))
        {
            reduced = "c_" + reduced;
        }

        if (reduced.Length > MaxLength)
        {
            var digest = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(name)))[..HashLength];
            reduced = string.Create(CultureInfo.InvariantCulture, $"{reduced[..(MaxLength - HashLength - 1)]}_{digest}");
        }

        return reduced;
    }

    /// <summary>Keeps the first of equal names and numbers the rest (<c>age</c>, <c>age_2</c>, <c>age_3</c>), within 63 characters.</summary>
    public static IReadOnlyList<string> Deduplicate(IEnumerable<string> names)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var name in names)
        {
            var candidate = name;
            for (var counter = 2; taken.Contains(candidate); counter++)
            {
                var suffix = string.Create(CultureInfo.InvariantCulture, $"_{counter}");
                candidate = name[..Math.Min(name.Length, MaxLength - suffix.Length)] + suffix;
            }

            taken.Add(candidate);
            result.Add(candidate);
        }

        return result;
    }

    /// <summary>Table names chosen by people must already be valid: scripts and dashboards select them by name.</summary>
    public static bool IsValidTableName(string? name) => name is not null && TableName().IsMatch(name);

    /// <summary>
    /// A name a published column can have once reduced: lowercase letters, digits and underscores, not starting with a digit, at most
    /// 63 characters (<c>tamiza.names</c>' identifier rule). Kobo's own columns start with an underscore (<c>_version</c>).
    /// </summary>
    public static bool IsValidColumnName(string? name) => name is not null && ColumnName().IsMatch(name);

    [GeneratedRegex("[^a-z0-9_]+")]
    private static partial Regex Invalid();

    [GeneratedRegex("__+")]
    private static partial Regex RepeatedUnderscores();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex TableName();

    // \z, not $: $ also matches before a final line break.
    [GeneratedRegex(@"^[a-z_][a-z0-9_]{0,62}\z")]
    private static partial Regex ColumnName();
}
