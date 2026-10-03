using Tamiza.Api.Kobo;

namespace Tamiza.Api.UnitTests.Kobo;

public sealed class KoboDataQueryTests
{
    [Fact]
    public void Without_filters_the_query_is_empty() =>
        Assert.Equal("{}", KoboDataQuery.Query(null, null));

    [Fact]
    public void The_cutoff_is_written_in_utc_without_an_offset()
    {
        var cutoff = new DateTimeOffset(2026, 10, 1, 4, 30, 15, TimeSpan.FromHours(-5));

        Assert.Equal("""{"_submission_time":{"$gte":"2026-10-01T09:30:15"}}""", KoboDataQuery.Query(cutoff, null));
    }

    [Fact]
    public void Keyset_paging_continues_after_the_last_id()
    {
        Assert.Equal("""{"_submission_time":{"$gte":"2026-10-01T00:00:00"},"_id":{"$gt":4021}}""",
            KoboDataQuery.Query(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), 4021));
    }

    [Fact]
    public void The_url_sorts_by_id_and_escapes_its_json_parameters()
    {
        var url = KoboDataQuery.PageUrl("https://kf.kobotoolbox.org", "aAsset1", null, 10, idsOnly: false, pageSize: 500);

        Assert.Equal(
            "https://kf.kobotoolbox.org/api/v2/assets/aAsset1/data/?format=json&limit=500"
            + "&sort=%7B%22_id%22%3A1%7D&query=%7B%22_id%22%3A%7B%22%24gt%22%3A10%7D%7D",
            url);
    }

    [Fact]
    public void Reading_ids_only_asks_for_the_id_field() =>
        Assert.EndsWith("&fields=%5B%22_id%22%5D", KoboDataQuery.PageUrl("https://kf.kobotoolbox.org", "a", null, null, idsOnly: true, pageSize: 5000));
}
