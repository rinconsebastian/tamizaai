using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Tamiza.Api.Http;
using Tamiza.Api.Mcp;

namespace Tamiza.Api.UnitTests.Mcp;

public sealed class McpResultsTests
{
    private sealed record Payload(string Name);

    [Fact]
    public void An_ok_result_gives_its_value()
    {
        var value = McpResults.Unwrap(TypedResults.Ok(new Payload("a")));

        Assert.Equal(new Payload("a"), value);
    }

    [Fact]
    public void A_created_or_accepted_result_gives_its_value()
    {
        Assert.Equal(new Payload("c"), McpResults.Unwrap(TypedResults.Created("/x", new Payload("c"))));
        Assert.Equal(new Payload("d"), McpResults.Unwrap(TypedResults.Accepted("/x", new Payload("d"))));
    }

    [Fact]
    public void A_result_without_a_body_gives_nothing()
    {
        Assert.Null(McpResults.Unwrap(TypedResults.NoContent()));
        Assert.Null(McpResults.Unwrap(TypedResults.Accepted("/x")));
    }

    [Fact]
    public void The_union_a_handler_returns_is_opened_to_the_result_inside()
    {
        Results<Ok<Payload>, ValidationProblem, ProblemHttpResult, NotFound> ok = TypedResults.Ok(new Payload("u"));
        Results<Ok<Payload>, ValidationProblem, ProblemHttpResult, NotFound> missing = TypedResults.NotFound();

        Assert.Equal(new Payload("u"), McpResults.Unwrap<Payload>(ok));
        Assert.Equal(McpRefusal.NotFoundCode, Assert.Throws<McpRefusal>(() => McpResults.Unwrap(missing)).Code);
    }

    [Fact]
    public void A_validation_problem_keeps_its_fields_and_messages()
    {
        var problem = Problems.Validation(new Dictionary<string, string[]>
        {
            ["config.groupBy"] = ["Choose a column to group by."],
            ["title"] = ["Enter a title."],
        });

        var refusal = Assert.Throws<McpRefusal>(() => McpResults.Unwrap(problem));

        Assert.Equal(McpRefusal.ValidationCode, refusal.Code);
        Assert.Contains("config.groupBy: Choose a column to group by.", refusal.Message);
        Assert.Contains("title: Enter a title.", refusal.Message);
        var fields = Assert.IsAssignableFrom<IDictionary<string, string[]>>(refusal.Details["fields"]);
        Assert.Equal(["Choose a column to group by."], fields["config.groupBy"]);
    }

    [Fact]
    public void A_problem_with_a_code_keeps_the_code_the_detail_and_the_extensions()
    {
        var problem = Problems.Status(StatusCodes.Status429TooManyRequests, "dashboards.limit", "Too many dashboards.", "A project has at most 7 dashboards.",
            new Dictionary<string, object?> { ["max"] = 7 });

        var refusal = Assert.Throws<McpRefusal>(() => McpResults.Unwrap(problem));

        Assert.Equal("dashboards.limit", refusal.Code);
        Assert.Equal("A project has at most 7 dashboards.", refusal.Message);
        Assert.Equal(7, refusal.Details["max"]);
        Assert.Equal(StatusCodes.Status429TooManyRequests, refusal.Details["status"]);
    }

    [Fact]
    public void A_problem_without_a_code_uses_the_title_and_the_generic_code()
    {
        var problem = TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict.");

        var refusal = Assert.Throws<McpRefusal>(() => McpResults.Unwrap(problem));

        Assert.Equal(McpRefusal.FailedCode, refusal.Code);
        Assert.Equal("Conflict.", refusal.Message);
    }

    [Fact]
    public void A_problem_that_is_a_missing_item_reads_as_not_found()
    {
        var problem = TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Widget not found.");

        var refusal = Assert.Throws<McpRefusal>(() => McpResults.Unwrap(problem));

        Assert.Equal(McpRefusal.NotFoundCode, refusal.Code);
        Assert.Equal("Widget not found was not found.", refusal.Message.Replace("Widget not found was", "Widget not found was"));
    }

    [Fact]
    public void A_plain_not_found_is_not_found()
    {
        Assert.Equal(McpRefusal.NotFoundCode, Assert.Throws<McpRefusal>(() => McpResults.Unwrap(TypedResults.NotFound())).Code);
    }

    [Fact]
    public void Any_other_status_is_a_failure_with_that_status()
    {
        var refusal = Assert.Throws<McpRefusal>(() => McpResults.Unwrap(TypedResults.Conflict()));

        Assert.Equal(McpRefusal.FailedCode, refusal.Code);
        Assert.Equal(StatusCodes.Status409Conflict, refusal.Details["status"]);
    }

    [Fact]
    public void A_typed_unwrap_refuses_a_result_of_another_type()
    {
        Assert.Equal(McpRefusal.FailedCode, Assert.Throws<McpRefusal>(() => McpResults.Unwrap<Payload>(TypedResults.NoContent())).Code);
    }
}
