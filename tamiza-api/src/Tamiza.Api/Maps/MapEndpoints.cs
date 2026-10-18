using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.Maps;

/// <param name="Source"><c>osm</c> or <c>google</c>. For Google, the page shows the logo and the credits from <c>GET /basemaps/{kind}/credits</c>, so there is no text.</param>
/// <param name="Text">OpenStreetMap's credit line; null for Google.</param>
/// <param name="Url">The address the credit links to; null for Google.</param>
public sealed record BasemapAttribution(string Source, string? Text, string? Url);

/// <summary>One background map a deployment offers.</summary>
/// <param name="Kind"><c>streets</c> or <c>satellite</c>.</param>
/// <param name="Tiles">A tile address template with <c>{z}</c>, <c>{x}</c> and <c>{y}</c>. For Google it is on this api and holds a tile link.</param>
/// <param name="LinksExpireAt">Google only: when the link in <paramref name="Tiles"/> stops working.</param>
/// <param name="LinksRefreshInSeconds">Google only: seconds from now until a new list holds a new link. Relative, so a client clock that is off does not matter.</param>
public sealed record Basemap(
    string Kind, int MinZoom, int MaxZoom, string Tiles, DateTimeOffset? LinksExpireAt, int? LinksRefreshInSeconds, BasemapAttribution Attribution);

/// <param name="Provider"><c>none</c>, <c>osm</c> or <c>google</c>.</param>
public sealed record BasemapList(string Provider, IReadOnlyList<Basemap> Basemaps);

/// <param name="Text">Google's data credits for the area; shown as text.</param>
/// <param name="MaxZoom">The deepest zoom Google has imagery for there; tiles deeper than this are never requested.</param>
public sealed record BasemapCredits(string Text, int MaxZoom);

/// <summary>Codes of the Problem Details the basemap routes answer with. The UI translates them.</summary>
public static class MapErrorCodes
{
    /// <summary>The user is over their tile or credits allowance (429, with <c>Retry-After</c>).</summary>
    public const string RateLimited = "basemaps.rateLimited";

    /// <summary>Google refuses for quota, billing or permission (503).</summary>
    public const string Unavailable = "basemaps.unavailable";

    /// <summary>Google could not be reached or gave no usable answer (502).</summary>
    public const string Upstream = "basemaps.upstream";
}

/// <summary>The background maps under the dashboard's maps: the list, Google's relayed tiles and their credits (design D13, D15).</summary>
public static class MapEndpoints
{
    public const string OsmTiles = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public const string OsmCopyright = "https://www.openstreetmap.org/copyright";
    public const int OsmMaxZoom = 19;
    public const string TilePath = "/api/v1/basemap-tiles";

    public static RouteGroupBuilder MapMapEndpoints(this RouteGroupBuilder api)
    {
        var maps = api.MapGroup("").WithTags("Maps");
        maps.MapGet("/basemaps", ListAsync).WithName("ListBasemaps");
        maps.MapGet("/basemaps/{kind}/credits", GetCreditsAsync).WithName("GetBasemapCredits");

        // Public: a browser loads a tile as an image and cannot send a token. The link in the address is the credential.
        maps.MapGet("/basemap-tiles/{link}/{z}/{x}/{y}", GetTileAsync).AllowAnonymous().WithName("GetBasemapTile");
        return api;
    }

    /// <summary>
    /// The basemaps the deployment offers to a member of any project or a superadmin; an empty list for anyone else, who gets no
    /// link either. With Google, one link per basemap, the same string until its window ends.
    /// </summary>
    private static async Task<Ok<BasemapList>> ListAsync(
        IOptions<MapOptions> options, ICurrentUser user, TamizaDbContext db, TileLinks links, TimeProvider time, CancellationToken ct)
    {
        var settings = options.Value;
        var provider = settings.EffectiveProvider;
        if (provider == MapOptions.ProviderNone
            || !(user.IsSuperAdmin || await db.ProjectMembers.AsNoTracking().AnyAsync(m => m.UserId == user.Id, ct)))
        {
            return TypedResults.Ok(new BasemapList(provider, []));
        }

        if (provider == MapOptions.ProviderOsm)
        {
            return TypedResults.Ok(new BasemapList(provider,
            [
                new Basemap(BasemapKinds.Streets, 0, OsmMaxZoom, OsmTiles, null, null,
                    new BasemapAttribution(MapOptions.ProviderOsm, "© OpenStreetMap contributors", OsmCopyright)),
            ]));
        }

        var basemaps = new List<Basemap>();
        foreach (var kind in BasemapKinds.All)
        {
            var link = links.Issue(user.Id, kind);
            basemaps.Add(new Basemap(
                kind, 0, GoogleTiles.MaxZoom, $"{TilePath}/{link.Link}/{{z}}/{{x}}/{{y}}", link.ExpiresAt, (int)Math.Max(1, Math.Ceiling((link.WindowEnd - time.GetUtcNow()).TotalSeconds)),
                new BasemapAttribution(MapOptions.ProviderGoogle, null, null)));
        }

        return TypedResults.Ok(new BasemapList(provider, basemaps));
    }

    /// <summary>
    /// One Google tile, relayed (design D15). The checks run in this order, and each failure is a 404 without any call to Google
    /// until the limits: the provider is Google, the coordinates are in range, the link is one this api issued and has not expired.
    /// </summary>
    private static async Task<IResult> GetTileAsync(
        string link, string z, string x, string y, HttpContext http, IOptions<MapOptions> options, TileLinks links, TileLimits limits,
        GoogleTiles google, CancellationToken ct)
    {
        if (!options.Value.IsGoogle || !TryTile(z, x, y, out var zoom, out var column, out var row))
        {
            return TypedResults.NotFound();
        }

        if (links.Read(link) is not { } claims)
        {
            return TypedResults.NotFound();
        }

        var decision = limits.TryTile(claims.UserId);
        if (!decision.Allowed)
        {
            return MapProblems.RateLimited(decision.RetryAfterSeconds);
        }

        var tile = await google.GetTileAsync(claims.Kind, zoom, column, row, http.Request.Headers.IfNoneMatch.ToString(), ct);
        return tile.Outcome switch
        {
            GoogleOutcome.Ok or GoogleOutcome.NotModified => new TileHttpResult(tile),
            GoogleOutcome.Missing => TypedResults.NotFound(),
            GoogleOutcome.Unavailable => MapProblems.Unavailable(),
            _ => MapProblems.Upstream(),
        };
    }

    /// <summary>The data credits and the imagery depth Google gives for the area on screen.</summary>
    private static async Task<IResult> GetCreditsAsync(
        string kind, string? zoom, string? north, string? south, string? east, string? west, IOptions<MapOptions> options, ICurrentUser user,
        TileLimits limits, GoogleTiles google, CancellationToken ct)
    {
        if (!options.Value.IsGoogle || !BasemapKinds.All.Contains(kind))
        {
            return TypedResults.NotFound();
        }

        // The values are read here, not by the framework, so a malformed one is a 400 naming the field like any other bad value.
        var errors = new FieldErrors();
        if (!int.TryParse(zoom, NumberStyles.None, CultureInfo.InvariantCulture, out var zoomLevel) || zoomLevel > GoogleTiles.MaxZoom)
        {
            errors.Add("zoom", $"The zoom must be a whole number from 0 to {GoogleTiles.MaxZoom}.");
        }

        var northDegrees = ReadDegrees(errors, "north", north, -90, 90);
        var southDegrees = ReadDegrees(errors, "south", south, -90, 90);
        var eastDegrees = ReadDegrees(errors, "east", east, -180, 180);
        var westDegrees = ReadDegrees(errors, "west", west, -180, 180);
        if (!errors.Any && southDegrees > northDegrees)
        {
            errors.Add("south", "The south must not be above the north.");
        }

        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var decision = limits.TryCredits(user.Id);
        if (!decision.Allowed)
        {
            return MapProblems.RateLimited(decision.RetryAfterSeconds);
        }

        var credits = await google.GetCreditsAsync(kind, zoomLevel, northDegrees, southDegrees, eastDegrees, westDegrees, ct);
        return credits.Outcome switch
        {
            GoogleOutcome.Ok => TypedResults.Ok(new BasemapCredits(credits.Text ?? "", credits.MaxZoom)),
            GoogleOutcome.Unavailable => MapProblems.Unavailable(),
            _ => MapProblems.Upstream(),
        };
    }

    private static double ReadDegrees(FieldErrors errors, string field, string? text, double min, double max)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < min || value > max)
        {
            errors.Add(field, string.Create(CultureInfo.InvariantCulture, $"The {field} must be a number from {min} to {max}."));
        }

        return value;
    }

    /// <summary>Whole numbers only, no sign or spaces, and the tile inside the map: <c>0 &lt;= x, y &lt; 2^z</c> for <c>0 &lt;= z &lt;= 22</c>.</summary>
    private static bool TryTile(string z, string x, string y, out int zoom, out int column, out int row)
    {
        column = row = 0;
        if (!TryWhole(z, out zoom) || zoom > GoogleTiles.MaxZoom || !TryWhole(x, out column) || !TryWhole(y, out row))
        {
            return false;
        }

        var size = 1L << zoom;
        return column < size && row < size;
    }

    private static bool TryWhole(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>A Google tile as the browser gets it: Google's content type and cache rules, and the mark that only this deployment's pages may use it.</summary>
    private sealed class TileHttpResult(GoogleTile tile) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.StatusCode = tile.Outcome == GoogleOutcome.NotModified ? StatusCodes.Status304NotModified : StatusCodes.Status200OK;
            if (tile.CacheControl is not null)
            {
                response.Headers.CacheControl = tile.CacheControl;
            }

            if (tile.ETag is not null)
            {
                response.Headers.ETag = tile.ETag;
            }

            response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            if (tile.Body is { } body)
            {
                response.ContentType = tile.ContentType;
                response.ContentLength = body.Length;
                await response.Body.WriteAsync(body, httpContext.RequestAborted);
            }
        }
    }
}

/// <summary>The Problem Details of the basemap routes. They never hold what Google said.</summary>
internal static class MapProblems
{
    public static IResult RateLimited(int retryAfterSeconds) => new ProblemWithRetryAfter(
        Problems.Status(StatusCodes.Status429TooManyRequests, MapErrorCodes.RateLimited,
            "Too many map tiles.", "This user has used up the map tiles allowed for now. Try again later.",
            new Dictionary<string, object?> { ["retryAfter"] = retryAfterSeconds }),
        retryAfterSeconds);

    public static ProblemHttpResult Unavailable() =>
        Problems.Status(StatusCodes.Status503ServiceUnavailable, MapErrorCodes.Unavailable,
            "The basemap is not available.", "The map provider refused the request. Contact the administrator.");

    public static ProblemHttpResult Upstream() =>
        Problems.Status(StatusCodes.Status502BadGateway, MapErrorCodes.Upstream,
            "The basemap could not be loaded.", "The map provider could not be reached or gave no usable answer.");

    /// <summary>A Problem Details response that also sets the <c>Retry-After</c> header, in seconds.</summary>
    private sealed class ProblemWithRetryAfter(ProblemHttpResult problem, int seconds) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            return problem.ExecuteAsync(httpContext);
        }
    }
}
