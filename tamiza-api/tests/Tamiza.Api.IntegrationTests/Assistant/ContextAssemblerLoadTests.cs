using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Assistant;
using Tamiza.Api.IntegrationTests.CleanData;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Assistant;

/// <summary>What <see cref="ContextAssembler"/> reads from the database: the published tables, the form, the time zone and the saved script 1.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ContextAssemblerLoadTests(PostgresFixture postgres)
{
    private static ContextRequest Request(string kind) =>
        new(kind, kind == "indicator" ? "piped_water_rate" : null, "script", [new AssistantMessage("user", "rate by municipality")], "def compute(cleaned, ctx):\n    return cleaned[\"main\"]\n");

    [Fact]
    public async Task The_context_describes_the_published_tables_the_form_the_time_zone_and_the_saved_script_1()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres, timeZone: "America/Bogota");
        await h.PublishAsync("clean", ("main", "_id bigint, water_source text", "(1, 'Piped'), (2, 'Well')"));
        await PublishedTables.CommentAsync(h.Db, h.ProjectId, "clean", "main", "water_source", "What is your main water source?");
        await using (var scope = h.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tamiza.Api.Data.TamizaDbContext>();
            var project = await db.Projects.SingleAsync(p => p.Id == h.ProjectId);
            project.FormFields = [new("water_source", "water_source", "select_one_water_list", "What is your main water source?", "water_list", OrOther: true)];
            project.FormChoices = [new("water_list", "piped", "Piped"), new("water_list", "well", "Well")];
            project.EnumeratorField = "group_a/enumerator";
            var author = await db.Users.Select(u => u.Id).FirstAsync();
            db.ScriptVersions.Add(new Tamiza.Api.Data.ScriptVersion
            {
                Id = Guid.NewGuid(),
                ProjectId = h.ProjectId,
                Kind = Tamiza.Api.Data.ScriptKind.Clean,
                Number = 1,
                Code = "def clean(raw, ctx):\n    return raw\n",
                AuthorId = author,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var read = h.Factory.Services.CreateAsyncScope();
        var context = await read.ServiceProvider.GetRequiredService<ContextAssembler>()
            .AssembleAsync(h.ProjectId, Request("indicator"), 1_000_000, CancellationToken.None);

        Assert.True(context.Fits);
        Assert.Contains("main: 2 rows", context.System);
        Assert.Contains("  water_source: text \"What is your main water source?\" [select_one water_list] [or other]", context.System);
        Assert.Contains("  water_list: Piped | Well", context.System);
        Assert.Contains("Project time zone: America/Bogota", context.System);
        Assert.Contains("Enumerator question: group_a/enumerator", context.System);
        Assert.Contains("<script1>\nThe saved script 1.", context.System);
        Assert.Contains("def clean(raw, ctx):", context.System);
    }

    [Fact]
    public async Task A_project_that_never_ran_lists_the_forms_questions()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await using (var scope = h.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tamiza.Api.Data.TamizaDbContext>();
            var project = await db.Projects.SingleAsync(p => p.Id == h.ProjectId);
            project.FormFields = [new("water_source", "water_source", "select_one_water_list", "What is your main water source?", "water_list")];
            project.FormChoices = [new("water_list", "piped", "Piped")];
            await db.SaveChangesAsync();
        }

        await using var read = h.Factory.Services.CreateAsyncScope();
        var context = await read.ServiceProvider.GetRequiredService<ContextAssembler>()
            .AssembleAsync(h.ProjectId, Request("clean"), 1_000_000, CancellationToken.None);

        Assert.Contains("No input tables are known yet", context.System);
        Assert.Contains("  water_source: select_one_water_list \"What is your main water source?\" [select_one water_list]", context.System);
        Assert.Contains("  water_list: Piped", context.System);
        Assert.DoesNotContain("<script1>\n", context.System);
    }
}
