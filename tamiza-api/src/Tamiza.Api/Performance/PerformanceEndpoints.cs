using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Performance;

public static class PerformanceEndpoints
{
    public static RouteGroupBuilder MapPerformanceEndpoints(this RouteGroupBuilder api)
    {
        var project = api.MapGroup("/projects/{projectId:guid}").WithTags("Performance");
        project.MapGet("/performance", GetAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetPerformanceDashboard");
        return api;
    }

    /// <param name="first">First day of the range, <c>YYYY-MM-DD</c> (query parameter <c>from</c>).</param>
    /// <param name="last">Last day of the range, <c>YYYY-MM-DD</c> (query parameter <c>to</c>).</param>
    private static async Task<Results<Ok<PerformanceDashboardResult>, ValidationProblem, ProblemHttpResult>> GetAsync(
        Guid projectId, [FromQuery(Name = "from")] string? first, [FromQuery(Name = "to")] string? last,
        TamizaDbContext db, PerformanceDashboard dashboard, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var (from, to) = PerformanceResults.ParseRange(first, last, errors);
        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        try
        {
            return TypedResults.Ok(await dashboard.GetAsync(db, projectId, from, to, ct));
        }
        catch (DashboardTimeoutException)
        {
            return Problems.Unavailable(DashboardTimeoutException.Code, "Computing the dashboard took too long. Try a shorter range or try again in a moment.");
        }
    }
}
