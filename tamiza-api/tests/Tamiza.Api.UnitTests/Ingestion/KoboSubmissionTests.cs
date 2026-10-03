using System.Text.Json;
using Tamiza.Api.Ingestion;

namespace Tamiza.Api.UnitTests.Ingestion;

public sealed class KoboSubmissionTests
{
    private static (SubmissionRow? Row, string? Error) Parse(string json) =>
        KoboSubmission.Parse(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void A_complete_submission_yields_every_field()
    {
        const string json = """
            {"_id": 4021, "_uuid": "b0c1e9a2-7f1e-4c55-9a51-2f1d3c4b5a6e", "__version__": "vXyZ123",
             "_submission_time": "2026-05-01T10:15:00", "municipality": "cali"}
            """;

        var (row, error) = Parse(json);

        Assert.Null(error);
        Assert.Equal(4021, row!.KoboId);
        Assert.Equal("b0c1e9a2-7f1e-4c55-9a51-2f1d3c4b5a6e", row.KoboUuid);
        Assert.Equal("vXyZ123", row.FormVersion);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 10, 15, 0, TimeSpan.Zero), row.SubmittedAt);
        Assert.Equal(TimeSpan.Zero, row.SubmittedAt!.Value.Offset);
        Assert.Equal(json.Trim(), row.Data);
    }

    [Theory]
    [InlineData("""{"_uuid": "u-1"}""")]
    [InlineData("""{"_id": "4021", "_uuid": "u-1"}""")]
    [InlineData("""{"_id": 40.5, "_uuid": "u-1"}""")]
    public void A_missing_or_non_integer_id_is_rejected(string json)
    {
        var (row, error) = Parse(json);

        Assert.Null(row);
        Assert.Contains("_id", error);
    }

    [Theory]
    [InlineData("""{"_id": 1}""")]
    [InlineData("""{"_id": 1, "_uuid": ""}""")]
    [InlineData("""{"_id": 1, "_uuid": 7}""")]
    public void A_missing_or_empty_uuid_is_rejected(string json)
    {
        var (row, error) = Parse(json);

        Assert.Null(row);
        Assert.Contains("_uuid", error);
    }

    [Fact]
    public void Something_other_than_an_object_is_rejected() =>
        Assert.Contains("object", Parse("[1, 2]").Error);

    [Fact]
    public void A_missing_version_and_time_are_stored_as_null()
    {
        var (row, _) = Parse("""{"_id": 1, "_uuid": "u-1"}""");

        Assert.Null(row!.FormVersion);
        Assert.Null(row.SubmittedAt);
    }

    [Fact]
    public void A_time_with_an_offset_is_converted_to_utc()
    {
        var (row, _) = Parse("""{"_id": 1, "_uuid": "u-1", "_submission_time": "2026-05-01T05:15:00-05:00"}""");

        Assert.Equal(new DateTimeOffset(2026, 5, 1, 10, 15, 0, TimeSpan.Zero), row!.SubmittedAt);
    }

    [Fact]
    public void Nul_characters_are_dropped_from_strings_and_nothing_else_changes()
    {
        var (row, _) = Parse("""{"_id": 1, "_uuid": "u-1", "note": "a\u0000b", "path": "C:\\u0000x", "n": [1, {"k": "\u0000"}]}""");

        using var stored = JsonDocument.Parse(row!.Data);
        Assert.Equal("ab", stored.RootElement.GetProperty("note").GetString());
        Assert.Equal("C:\\u0000x", stored.RootElement.GetProperty("path").GetString());
        Assert.Equal("", stored.RootElement.GetProperty("n")[1].GetProperty("k").GetString());
        Assert.DoesNotContain('\0', row.Data);
    }
}
