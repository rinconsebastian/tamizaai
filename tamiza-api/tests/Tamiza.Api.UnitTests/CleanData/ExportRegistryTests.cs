using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.UnitTests.CleanData;

public sealed class ExportRegistryTests : IDisposable
{
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tamiza-registry-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ManualClock _clock = new();
    private readonly ExportRegistry _registry;

    public ExportRegistryTests()
    {
        _registry = new ExportRegistry(_clock, Options.Create(new DataDashboardOptions { ExportDirectory = _directory, ExportLinkLifetime = TimeSpan.FromMinutes(5) }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string File(string content = "data")
    {
        var path = _registry.NewFilePath();
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    private string Add(string? path = null, Guid? project = null) =>
        _registry.Add(project ?? Guid.NewGuid(), Guid.NewGuid(), path ?? File(), "persons.csv", "text/csv").Token;

    [Fact]
    public void A_link_gives_its_file_once()
    {
        var project = Guid.NewGuid();
        var path = File();
        var token = Add(path, project);

        var first = _registry.TryTake(token);
        var second = _registry.TryTake(token);

        Assert.NotNull(first);
        Assert.Equal((project, path, "persons.csv", "text/csv"), (first.ProjectId, first.Path, first.FileName, first.ContentType));
        Assert.Null(second);
    }

    [Fact]
    public void A_token_that_was_never_issued_gives_nothing()
    {
        Add();

        Assert.Null(_registry.TryTake("not-a-token"));
        Assert.Null(_registry.TryTake(""));
        Assert.Null(_registry.TryTake(new string('A', 43)));
    }

    [Fact]
    public void Tokens_are_unguessable_urlsafe_and_different_each_time()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => Add()).ToList();

        Assert.Equal(200, tokens.Distinct().Count());
        Assert.All(tokens, t =>
        {
            Assert.Equal(43, t.Length); // 32 random bytes
            Assert.Matches("^[A-Za-z0-9_-]+$", t);
        });
    }

    [Fact]
    public void A_link_works_until_it_expires_and_not_after()
    {
        var early = Add();
        var late = Add();

        _clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.NotNull(_registry.TryTake(early));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(_registry.TryTake(late));
    }

    [Fact]
    public void An_expired_link_deletes_its_file()
    {
        var path = File();
        var token = Add(path);
        _clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Null(_registry.TryTake(token));

        Assert.False(System.IO.File.Exists(path));
    }

    [Fact]
    public async Task When_many_callers_race_for_one_link_exactly_one_wins()
    {
        for (var round = 0; round < 50; round++)
        {
            var token = Add();
            var wins = 0;

            await Parallel.ForEachAsync(Enumerable.Range(0, 16), new ParallelOptions { MaxDegreeOfParallelism = 16 }, (_, _) =>
            {
                if (_registry.TryTake(token) is not null)
                {
                    Interlocked.Increment(ref wins);
                }

                return ValueTask.CompletedTask;
            });

            Assert.Equal(1, wins);
        }
    }

    [Fact]
    public void The_sweep_deletes_the_files_of_expired_entries_and_keeps_the_others()
    {
        var oldPath = File();
        var old = Add(oldPath);
        _clock.Advance(TimeSpan.FromMinutes(3));
        var freshPath = File();
        var fresh = Add(freshPath);
        _clock.Advance(TimeSpan.FromMinutes(3)); // the first is 6 minutes old, the second 3

        var removed = _registry.SweepExpired();

        Assert.Equal(1, removed);
        Assert.False(System.IO.File.Exists(oldPath));
        Assert.True(System.IO.File.Exists(freshPath));
        Assert.Null(_registry.TryTake(old));
        Assert.NotNull(_registry.TryTake(fresh));
        Assert.Equal(0, _registry.Pending);
    }

    [Fact]
    public void The_sweep_of_nothing_removes_nothing()
    {
        Assert.Equal(0, _registry.SweepExpired());
    }

    [Fact]
    public void Clearing_the_directory_deletes_export_files_and_leaves_other_files()
    {
        var export = File();
        var other = Path.Combine(_directory, "keep-me.txt");
        System.IO.File.WriteAllText(other, "mine");

        var removed = _registry.ClearDirectory();

        Assert.Equal(1, removed);
        Assert.False(System.IO.File.Exists(export));
        Assert.True(System.IO.File.Exists(other));
    }

    [Fact]
    public void Clearing_a_directory_that_does_not_exist_does_nothing() =>
        Assert.Equal(0, _registry.ClearDirectory());

    [Fact]
    public void New_paths_are_in_the_directory_with_the_export_extension_and_never_repeat()
    {
        var paths = Enumerable.Range(0, 50).Select(_ => _registry.NewFilePath()).ToList();

        Assert.Equal(50, paths.Distinct().Count());
        Assert.All(paths, p =>
        {
            Assert.Equal(_directory, Path.GetDirectoryName(p));
            Assert.EndsWith(".export", p);
        });
    }

    [Fact]
    public void Deleting_a_file_that_is_gone_is_not_an_error()
    {
        Assert.False(ExportRegistry.DeleteQuietly(Path.Combine(_directory, "missing.export")));
    }

    [Fact]
    public async Task The_sweeper_clears_leftover_files_when_the_api_starts()
    {
        var leftover = File();
        var sweeper = new ExportSweeper(_registry, _clock, NullLogger<ExportSweeper>.Instance);

        await sweeper.StartAsync(CancellationToken.None);
        await sweeper.StopAsync(CancellationToken.None);

        Assert.False(System.IO.File.Exists(leftover));
    }
}

public sealed class ExportFileNameTests
{
    [Theory]
    [InlineData("Household survey 2026", "household-survey-2026")]
    [InlineData("Encuesta de Caracterización — Cali", "encuesta-de-caracterizacion-cali")]
    [InlineData("  --Leading and trailing--  ", "leading-and-trailing")]
    [InlineData("a/b\\c:d*e?\"f<g>h|i", "a-b-c-d-e-f-g-h-i")]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("日本語", "project")]
    [InlineData("", "project")]
    public void A_project_name_becomes_a_safe_ascii_slug(string name, string expected) =>
        Assert.Equal(expected, ExportFileName.Slug(name));

    [Fact]
    public void A_long_name_is_cut_without_a_trailing_dash()
    {
        var slug = ExportFileName.Slug(new string('a', 39) + " " + new string('b', 30));

        Assert.True(slug.Length <= 40);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public void The_file_name_is_the_project_the_table_and_the_day()
    {
        var name = ExportFileName.Create("Encuesta Cali", "persons", new DateOnly(2026, 9, 3), "xlsx");

        Assert.Equal("encuesta-cali_persons_2026-09-03.xlsx", name);
    }
}
