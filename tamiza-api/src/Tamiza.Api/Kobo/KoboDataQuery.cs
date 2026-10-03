using System.Globalization;
using System.Text.Json.Nodes;

namespace Tamiza.Api.Kobo;

/// <summary>Builds Kobo API v2 data URLs. Pages are read in <c>_id</c> order and continue after the last <c>_id</c> seen.</summary>
public static class KoboDataQuery
{
    /// <summary>
    /// The Mongo-style <c>query</c> parameter: submissions made at or after <paramref name="submittedSince"/> and with an
    /// <c>_id</c> above <paramref name="afterId"/>. Keyset paging keeps pages stable while submissions arrive or are deleted.
    /// </summary>
    public static string Query(DateTimeOffset? submittedSince, long? afterId)
    {
        var query = new JsonObject();
        if (submittedSince is { } since)
        {
            // Kobo stores _submission_time as UTC text without an offset, so it is compared in that form.
            query["_submission_time"] = new JsonObject
            {
                ["$gte"] = since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            };
        }

        if (afterId is { } after)
        {
            query["_id"] = new JsonObject { ["$gt"] = after };
        }

        return query.ToJsonString();
    }

    public static string PageUrl(string serverUrl, string assetUid, DateTimeOffset? submittedSince, long? afterId, bool idsOnly, int pageSize)
    {
        var url = $"{serverUrl}/api/v2/assets/{Uri.EscapeDataString(assetUid)}/data/?format=json&limit={pageSize}"
            + $"&sort={Uri.EscapeDataString("""{"_id":1}""")}"
            + $"&query={Uri.EscapeDataString(Query(submittedSince, afterId))}";
        return idsOnly ? url + $"&fields={Uri.EscapeDataString("""["_id"]""")}" : url;
    }
}
