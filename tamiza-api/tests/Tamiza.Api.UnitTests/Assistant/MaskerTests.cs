using System.Text;
using Tamiza.Api.Assistant;

namespace Tamiza.Api.UnitTests.Assistant;

public sealed class MaskerTests
{
    private static Masker Create(string draft = "", string[]? identifiers = null, string[]? labels = null) =>
        new(identifiers ?? [], labels ?? [], draft);

    [Fact]
    public void KnownColumnKept()
    {
        var masker = Create(identifiers: ["main", "water_source"]);

        Assert.Equal("KeyError: 'water_source'", masker.Mask("KeyError: 'water_source'"));
    }

    [Fact]
    public void IdentifiersAreComparedWithoutRegardToCase()
    {
        var masker = Create(identifiers: ["Water_Source"]);

        Assert.Equal("KeyError: 'WATER_SOURCE'", masker.Mask("KeyError: 'WATER_SOURCE'"));
    }

    [Fact]
    public void QuotedValueMasked()
    {
        Assert.Equal(
            "ValueError: could not convert string to float: '…'",
            Create().Mask("ValueError: could not convert string to float: '3001234567'"));
    }

    [Fact]
    public void UnquotedValueMasked()
    {
        Assert.Equal(
            "pandas._libs.tslibs.parsing.DateParseError: Unknown datetime string format, unable to parse: …",
            Create().Mask("pandas._libs.tslibs.parsing.DateParseError: Unknown datetime string format, unable to parse: Juan Perez"));
    }

    [Fact]
    public void MessageTheScriptWrote()
    {
        var masker = Create("""raise ValueError(f"only {n} rows, first {name}")""");

        Assert.Equal("ValueError: only 3 rows, first …", masker.Mask("ValueError: only 3 rows, first Ana Gómez"));
    }

    [Fact]
    public void NameSplitAcrossLabels()
    {
        // The draft compares with both labels, but a label's words are not unlocked by the draft.
        var draft = "main[main[\"municipality\"].isin([\"Santa Ana\", \"Gómez Plata\"])]";
        var masker = Create(draft, labels: ["Santa Ana", "Gómez Plata"]);

        Assert.Equal("ValueError: first …", masker.Mask("ValueError: first Ana Gómez"));
    }

    [Fact]
    public void ALabelsWordsAreKeptOnlyInsideTheWholeLabel()
    {
        var masker = Create("""x = "Santa Ana" """, labels: ["Santa Ana"]);

        Assert.Equal("a Santa Ana and …", masker.Mask("a Santa Ana and Ana"));
        Assert.Equal("…", masker.Mask("Santa"));
    }

    [Fact]
    public void WholeLabelKept()
    {
        var masker = Create(labels: ["Santa Ana"]);

        Assert.Equal(
            "ValueError: invalid literal for int() with base 10: 'Santa Ana'",
            masker.Mask("ValueError: invalid literal for int() with base 10: 'Santa Ana'"));
    }

    [Fact]
    public void ALabelIsFoundWhateverItsCaseAndSpacing()
    {
        var masker = Create(labels: ["Santa Ana"]);

        Assert.Equal("'SANTA   ANA' and 'santa\tana'", masker.Mask("'SANTA   ANA' and 'santa\tana'"));
    }

    [Fact]
    public void ALabelIsNotFoundInsideAWord()
    {
        var masker = Create(labels: ["Ana"]);

        Assert.Equal("…", masker.Mask("Banana"));
    }

    [Theory]
    [InlineData("1.023.456.789")]
    [InlineData("300 123 4567")]
    [InlineData("300 - 123 - 4567")]
    [InlineData("300-123-4567")]
    [InlineData("1985-03-12")]
    [InlineData("4.6097, -74.0817")]
    [InlineData("4.6097,-74.0817")]
    [InlineData("-74.0817 , 4.6097")]
    [InlineData("1204, 3051")]
    [InlineData("2026-09-12 10:30:15")]
    [InlineData("3001234567")]
    [InlineData("1234567")]
    public void SeparatedNumbers_ThatHaveMoreThanSixDigitsBecomeOneEllipsis(string number)
    {
        Assert.Equal("value …", Create().Mask($"value {number}"));
        Assert.Equal("value … rows", Create().Mask($"value {number} rows"));
    }

    [Theory]
    [InlineData("2026-09")]
    [InlineData("4,520")]
    [InlineData("123456")]
    [InlineData("12:30")]
    [InlineData("3")]
    [InlineData("10.0.0.5")]
    public void NumbersOfAtMostSixDigitsAreKept(string number)
    {
        Assert.Equal($"value {number} rows", Create().Mask($"value {number} rows"));
    }

    [Fact]
    public void ASignBelongsToTheNumber()
    {
        Assert.Equal("value …", Create().Mask("value -3001234567"));
        Assert.Equal("value …", Create().Mask("value +3001234567"));
    }

    [Fact]
    public void ANumberIsNotJoinedAcrossWords()
    {
        Assert.Equal("rows 3 and 4 and 3001 and 2000", Create().Mask("rows 3 and 4 and 3001 and 2000"));
    }

    [Theory]
    [InlineData("c_300_123_4567")]
    [InlineData("c_1985_03_12")]
    [InlineData("phone_3001234567")]
    [InlineData("value_1985_03_12")]
    public void UnderscoreTokensCountTheirDigitsTogether(string name)
    {
        Assert.Equal("column …", Create().Mask($"column {name}"));
    }

    [Fact]
    public void UnderscoreTokensAreKeptWhenEveryPartIs()
    {
        var masker = Create();

        Assert.Equal("ctx_log value_2026_09", masker.Mask("ctx_log value_2026_09"));
        Assert.Equal("…", masker.Mask("juan_perez"));
        Assert.Equal("…", masker.Mask("value_perez"));
        Assert.Equal("_id", Create(identifiers: ["_id"]).Mask("_id"));
        Assert.Equal("__init__", masker.Mask("__init__"));
    }

    [Fact]
    public void ATokenOfTheDraftIsKept()
    {
        var masker = Create("""municipality = "Barrio Norte"  # keep the north neighbourhood""");

        Assert.Equal("municipality Barrio Norte neighbourhood", masker.Mask("municipality Barrio Norte neighbourhood"));
        Assert.Equal("…", masker.Mask("Juan"));
    }

    [Fact]
    public void ARunOfMaskedWordsCollapsesIntoOneEllipsis()
    {
        Assert.Equal("first … done", Create().Mask("first Juan Perez Gómez done"));
        Assert.Equal("first … done", Create().Mask("first Juan  Perez   Gómez done"));
        Assert.Equal("'…'", Create().Mask("'Juan Perez'"));
        Assert.Equal("'…', '…'", Create().Mask("'Juan', 'Perez'"));
    }

    [Fact]
    public void QuotesAndPunctuationStay()
    {
        Assert.Equal("KeyError: '…'", Create().Mask("KeyError: 'Juan Perez'"));
        Assert.Equal("(…), […], {…}", Create().Mask("(Juan), [Perez], {Gomez}"));
    }

    [Fact]
    public void ContractionsAndTheArticleAreKept()
    {
        Assert.Equal("it doesn't match a format", Create().Mask("it doesn't match a format"));
        Assert.Equal("…'s match", Create().Mask("Juan's match"));
    }

    [Fact]
    public void OtherSingleLettersAreMasked()
    {
        Assert.Equal("value …", Create().Mask("value J"));
    }

    [Fact]
    public void ADecomposedNameCannotSlipThrough()
    {
        var decomposed = "Gómez";

        Assert.Equal("first …", Create().Mask($"first {decomposed}"));
    }

    [Fact]
    public void AVocabularyWordIsKeptWhateverItsCase()
    {
        Assert.Equal("KEYERROR Could Not Convert", Create().Mask("KEYERROR Could Not Convert"));
    }

    [Fact]
    public void ACustomVocabularyIsUsed()
    {
        var masker = new Masker([], [], "", new HashSet<string> { "hello" });

        Assert.Equal("Hello …", masker.Mask("Hello world"));
    }

    [Fact]
    public void EmptyTextComesBackAsItIs()
    {
        Assert.Equal("", Create().Mask(""));
        Assert.Equal("", Create().Mask(null));
    }

    [Fact]
    public void ManyLabelsAreProtectedWithoutTrouble()
    {
        var labels = Enumerable.Range(1, 1500).Select(number => $"Municipality number {number:0000} of the department").ToArray();
        var masker = Create(labels: labels);
        var text = new StringBuilder("ValueError: unknown ");
        text.Append("'Municipality number 0765 of the department'");

        Assert.Equal(text.ToString(), masker.Mask(text.ToString()));
        var other = masker.Mask("ValueError: unknown 'Municipality number 7765 of the department'");
        Assert.NotEqual("ValueError: unknown 'Municipality number 7765 of the department'", other);
        Assert.Contains("…", other);
    }

    [Fact]
    public void ALabelWithNoLetterIsNotProtected()
    {
        // A label such as "12" must not carve a number such as 300 12 4567 into pieces that each look harmless.
        var masker = Create(labels: ["12", "1985"]);

        Assert.Equal("value …", masker.Mask("value 300 12 4567"));
    }
}
