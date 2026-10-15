namespace Tamiza.Api.Data;

/// <summary>
/// A sample of a project's data for local work, prepared by the analytics worker like a test: queued here, claimed ahead of
/// queued runs, and kept as one zip (<see cref="SampleDownloadPart"/>) for an hour after it is ready.
/// </summary>
public sealed class SampleDownload
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid RequestedBy { get; set; }

    public int SampleSize { get; set; }

    public RunStatus Status { get; set; }

    /// <summary>The saved script 1 the worker ran on the sample; resolved when it claimed the download.</summary>
    public Guid? CleanVersionId { get; set; }

    public DateTimeOffset QueuedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>The size of the zip; set when the download is ready.</summary>
    public long? SizeBytes { get; set; }

    public string? FileSha256 { get; set; }

    /// <summary>What the zip holds, for the page; set when the download is ready.</summary>
    public SampleDownloadContents? Contents { get; set; }

    /// <summary><c>step.flatten</c>, <c>download.tooLarge</c> or <c>download.interrupted</c>.</summary>
    public string? ErrorCode { get; set; }

    public string? Error { get; set; }
}

/// <summary>One part of a prepared zip, in order. The parts of a download are deleted with it.</summary>
public sealed class SampleDownloadPart
{
    public Guid DownloadId { get; set; }

    public int Part { get; set; }

    public required byte[] Data { get; set; }
}

/// <summary>The manifest of a download, less the library versions: what the page tells the requester about the zip.</summary>
public sealed record SampleDownloadContents(
    SampleDownloadSample Sample,
    SampleDownloadTables Raw,
    SampleDownloadScript1 Clean,
    List<SampleDownloadReference> Reference);

/// <param name="Requested">The sample size that was asked for.</param>
/// <param name="Submissions">How many submissions the sample holds (fewer than requested in a small project).</param>
public sealed record SampleDownloadSample(int Requested, int Submissions);

public sealed record SampleDownloadTables(List<SampleDownloadTable> Tables);

public sealed record SampleDownloadTable(string Name, long Rows);

/// <summary><see cref="Status"/> is <c>succeeded</c>, <c>failed</c> (the saved script 1 raised on the sample) or <c>none</c> (the project has no script 1).</summary>
public sealed record SampleDownloadScript1(string Status, int? Version, string? Error, List<SampleDownloadTable> Tables);

public sealed record SampleDownloadReference(string Name, long Rows, DateTimeOffset UpdatedAt);
