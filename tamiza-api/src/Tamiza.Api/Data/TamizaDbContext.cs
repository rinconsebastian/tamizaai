using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tamiza.DbUp;

namespace Tamiza.Api.Data;

public sealed class TamizaDbContext(DbContextOptions<TamizaDbContext> options) : DbContext(options)
{
    /// <summary>Schema that holds the metadata tables owned by the API.</summary>
    public const string Schema = SchemaMigrator.Schema;

    private const string RoleCheck = "role IN ('admin', 'analyst', 'viewer')";

    public DbSet<User> Users => Set<User>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<ProjectInvitation> ProjectInvitations => Set<ProjectInvitation>();

    public DbSet<InvitedAccount> InvitedAccounts => Set<InvitedAccount>();

    public DbSet<SamplingFrame> SamplingFrames => Set<SamplingFrame>();

    public DbSet<RawSubmission> RawSubmissions => Set<RawSubmission>();

    public DbSet<Sync> Syncs => Set<Sync>();

    public DbSet<ScriptVersion> ScriptVersions => Set<ScriptVersion>();

    public DbSet<Run> Runs => Set<Run>();

    public DbSet<RunStep> RunSteps => Set<RunStep>();

    public DbSet<ScriptTest> ScriptTests => Set<ScriptTest>();

    public DbSet<SampleDownload> SampleDownloads => Set<SampleDownload>();

    public DbSet<SampleDownloadPart> SampleDownloadParts => Set<SampleDownloadPart>();

    public DbSet<AssistantCall> AssistantCalls => Set<AssistantCall>();

    public DbSet<ReferenceTable> ReferenceTables => Set<ReferenceTable>();

    public DbSet<ReferenceTableContent> ReferenceTableContents => Set<ReferenceTableContent>();

    public DbSet<FormVersion> FormVersions => Set<FormVersion>();

    public DbSet<Dashboard> Dashboards => Set<Dashboard>();

    public DbSet<DashboardWidget> DashboardWidgets => Set<DashboardWidget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        var roleConverter = LowercaseEnum<ProjectRole>();

        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).ValueGeneratedNever();
            user.Property(u => u.KeycloakSub).HasMaxLength(255);
            user.HasIndex(u => u.KeycloakSub).IsUnique();
            user.Property(u => u.Name).HasMaxLength(255);
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.EmailVerified).HasDefaultValue(false);
        });

        modelBuilder.Entity<Project>(project =>
        {
            project.ToTable("projects");
            project.HasKey(p => p.Id);
            project.Property(p => p.Id).ValueGeneratedNever();
            project.Property(p => p.Name).HasMaxLength(200);
            project.Property(p => p.KoboServerUrl).HasMaxLength(2048);
            project.Property(p => p.KoboAssetUid).HasMaxLength(64);
            project.Property(p => p.FormName).HasMaxLength(500);
            project.Property(p => p.FormFields).HasJsonColumn();
            project.Property(p => p.EnumeratorField).HasMaxLength(1024);
            project.Property(p => p.TimeZone).HasMaxLength(64).HasDefaultValue("UTC");
            project.Property(p => p.SyncIntervalHours).HasDefaultValue(12);
            project.Property(p => p.ReconcileIntervalHours).HasDefaultValue(24);
            project.Property(p => p.NextSyncAt).HasDefaultValueSql("now()");
            project.Property(p => p.NextReconcileAt).HasDefaultValueSql("(now() + interval '1 day')");
            project.Property(p => p.RunAfterSync).HasDefaultValue(true);
            project.Property(p => p.FormChoices).HasJsonColumn().HasDefaultValueSql("'[]'::jsonb");
            project.Property(p => p.FormVersion).HasMaxLength(128);
            project.ToTable(table =>
            {
                table.HasCheckConstraint("ck_projects_sync_interval_hours", "sync_interval_hours BETWEEN 1 AND 168");
                table.HasCheckConstraint("ck_projects_reconcile_interval_hours", "reconcile_interval_hours BETWEEN 1 AND 168");
                table.HasCheckConstraint("ck_projects_run_interval_hours", "run_interval_hours BETWEEN 1 AND 168");
            });
            project.HasOne<User>().WithMany().HasForeignKey(p => p.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectMember>(member =>
        {
            member.ToTable("project_members", table => table.HasCheckConstraint("ck_project_members_role", RoleCheck));
            member.HasKey(m => new { m.ProjectId, m.UserId });
            member.Property(m => m.Role).HasConversion(roleConverter).HasMaxLength(16);
            member.HasOne<Project>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
            member.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
            member.HasIndex(m => m.UserId);
        });

        modelBuilder.Entity<ProjectInvitation>(invitation =>
        {
            invitation.ToTable("project_invitations", table => table.HasCheckConstraint("ck_project_invitations_role", RoleCheck));
            invitation.HasKey(i => i.Id);
            invitation.Property(i => i.Id).ValueGeneratedNever();
            invitation.Property(i => i.Email).HasMaxLength(320);
            invitation.Property(i => i.NormalizedEmail).HasMaxLength(320);
            invitation.Property(i => i.Role).HasConversion(roleConverter).HasMaxLength(16);
            invitation.HasIndex(i => new { i.ProjectId, i.NormalizedEmail }).IsUnique();
            invitation.HasIndex(i => i.NormalizedEmail);
            invitation.HasOne<Project>().WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Cascade);
            invitation.HasOne<User>().WithMany().HasForeignKey(i => i.InvitedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InvitedAccount>(account =>
        {
            account.ToTable("invited_accounts");
            account.HasKey(a => a.AccountId);
            account.Property(a => a.AccountId).HasMaxLength(255).ValueGeneratedNever();
            account.Property(a => a.NormalizedEmail).HasMaxLength(320);
            account.HasIndex(a => a.NormalizedEmail);
            account.HasIndex(a => a.CreatedBy);
            account.HasOne<User>().WithMany().HasForeignKey(a => a.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SamplingFrame>(frame =>
        {
            frame.ToTable("sampling_frames");
            frame.HasKey(f => f.ProjectId);
            frame.Property(f => f.Dimensions).HasJsonColumn();
            frame.Property(f => f.Targets).HasJsonColumn();
            frame.HasOne<Project>().WithOne().HasForeignKey<SamplingFrame>(f => f.ProjectId).OnDelete(DeleteBehavior.Cascade);
            frame.HasOne<User>().WithMany().HasForeignKey(f => f.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RawSubmission>(submission =>
        {
            submission.ToTable("raw_submissions", table => table.HasCheckConstraint("ck_raw_submissions_origin", "origin IN ('webhook', 'sync')"));
            submission.HasKey(s => s.Id);
            submission.Property(s => s.Id).UseIdentityByDefaultColumn();
            submission.Property(s => s.KoboUuid).HasMaxLength(128);
            submission.Property(s => s.FormVersion).HasMaxLength(128);
            submission.Property(s => s.Data).HasColumnType("jsonb");
            submission.Property(s => s.Origin).HasConversion(LowercaseEnum<SubmissionOrigin>()).HasMaxLength(16);
            submission.Property(s => s.Deleted).HasDefaultValue(false);
            submission.HasIndex(s => new { s.ProjectId, s.KoboId }).IsUnique();
            submission.HasIndex(s => new { s.ProjectId, s.KoboUuid });
            // Per-version counts and the re-recording of versions at each synchronization read only live submissions.
            submission.HasIndex(s => new { s.ProjectId, s.FormVersion }).HasFilter("NOT deleted");
            // The performance dashboard's date range and daily counts read live submissions by reception time.
            submission.HasIndex(s => new { s.ProjectId, s.SubmittedAt }).HasFilter("NOT deleted");
            submission.HasOne<Project>().WithMany().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Sync>(sync =>
        {
            sync.ToTable("syncs", table =>
            {
                table.HasCheckConstraint("ck_syncs_kind", "kind IN ('incremental', 'full', 'reconciliation')");
                table.HasCheckConstraint("ck_syncs_trigger", "trigger IN ('schedule', 'manual', 'initial')");
                table.HasCheckConstraint("ck_syncs_status", "status IN ('queued', 'running', 'succeeded', 'failed')");
            });
            sync.HasKey(s => s.Id);
            sync.Property(s => s.Id).ValueGeneratedNever();
            sync.Property(s => s.Kind).HasConversion(LowercaseEnum<SyncKind>()).HasMaxLength(16);
            sync.Property(s => s.Trigger).HasConversion(LowercaseEnum<SyncTrigger>()).HasMaxLength(16);
            sync.Property(s => s.Status).HasConversion(LowercaseEnum<SyncStatus>()).HasMaxLength(16);
            sync.Property(s => s.CreatedCount).HasDefaultValue(0);
            sync.Property(s => s.UpdatedCount).HasDefaultValue(0);
            sync.Property(s => s.DeletedCount).HasDefaultValue(0);
            sync.Property(s => s.ErrorCode).HasMaxLength(64);
            // At most one queued or running synchronization per project; the scheduler and manual requests rely on it.
            sync.HasIndex(s => s.ProjectId).IsUnique().HasFilter("status IN ('queued', 'running')").HasDatabaseName("ix_syncs_project_id_active");
            sync.HasIndex(s => new { s.ProjectId, s.QueuedAt }).IsDescending(false, true);
            sync.HasOne<Project>().WithMany().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);
            sync.HasOne(s => s.RequestedByUser).WithMany().HasForeignKey(s => s.RequestedBy).OnDelete(DeleteBehavior.Restrict);
        });

        MapPipeline(modelBuilder);
    }

    private static void MapPipeline(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScriptVersion>(version =>
        {
            version.ToTable("script_versions", table =>
            {
                table.HasCheckConstraint("ck_script_versions_kind", "kind IN ('clean', 'output', 'indicator')");
                table.HasCheckConstraint("ck_script_versions_name", "(kind = 'indicator') = (name IS NOT NULL)");
                table.HasCheckConstraint("ck_script_versions_format", "format IS NULL OR format IN ('count', 'decimal', 'percent', 'currency')");
            });
            version.HasKey(v => v.Id);
            version.Property(v => v.Id).ValueGeneratedNever();
            version.Property(v => v.Kind).HasConversion(LowercaseEnum<ScriptKind>()).HasMaxLength(16);
            version.Property(v => v.Name).HasMaxLength(63);
            version.Property(v => v.Label).HasColumnType("text");
            version.Property(v => v.Description).HasColumnType("text");
            version.Property(v => v.Unit).HasMaxLength(32);
            version.Property(v => v.Format).HasMaxLength(16);
            // Versions are numbered per indicator; the clean and output scripts have no name and still collide.
            version.HasIndex(v => new { v.ProjectId, v.Kind, v.Name, v.Number }).IsUnique().AreNullsDistinct(false);
            version.HasOne<Project>().WithMany().HasForeignKey(v => v.ProjectId).OnDelete(DeleteBehavior.Cascade);
            version.HasOne(v => v.Author).WithMany().HasForeignKey(v => v.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Run>(run =>
        {
            run.ToTable("runs", table =>
            {
                table.HasCheckConstraint("ck_runs_trigger", "trigger IN ('manual', 'sync', 'schedule')");
                table.HasCheckConstraint("ck_runs_status", "status IN ('queued', 'running', 'succeeded', 'failed')");
            });
            run.HasKey(r => r.Id);
            run.Property(r => r.Id).ValueGeneratedNever();
            run.Property(r => r.Trigger).HasConversion(LowercaseEnum<RunTrigger>()).HasMaxLength(16);
            run.Property(r => r.Status).HasConversion(LowercaseEnum<RunStatus>()).HasMaxLength(16);
            run.Property(r => r.ErrorCode).HasMaxLength(64);
            run.Property(r => r.ReferenceTables).HasJsonColumn().HasDefaultValueSql("'[]'::jsonb");
            run.Property(r => r.IndicatorVersions).HasJsonColumn().HasDefaultValueSql("'[]'::jsonb");
            // At most one queued run per project; a running run may have one queued behind it.
            run.HasIndex(r => r.ProjectId).IsUnique().HasFilter("status = 'queued'").HasDatabaseName("ix_runs_project_id_queued");
            run.HasIndex(r => new { r.ProjectId, r.QueuedAt }).IsDescending(false, true);
            run.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
            run.HasOne(r => r.RequestedByUser).WithMany().HasForeignKey(r => r.RequestedBy).OnDelete(DeleteBehavior.Restrict);
            // NO ACTION: a project's deletion cascades to runs and script versions in the same statement.
            run.HasOne<ScriptVersion>().WithMany().HasForeignKey(r => r.CleanVersionId).OnDelete(DeleteBehavior.NoAction);
            run.HasOne<ScriptVersion>().WithMany().HasForeignKey(r => r.OutputVersionId).OnDelete(DeleteBehavior.NoAction);
            run.HasMany(r => r.Steps).WithOne().HasForeignKey(s => s.RunId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RunStep>(step =>
        {
            step.ToTable("run_steps", table =>
            {
                table.HasCheckConstraint("ck_run_steps_step", "step IN ('flatten', 'clean', 'output', 'indicator')");
                table.HasCheckConstraint("ck_run_steps_name", "(step = 'indicator') = (name <> '')");
                table.HasCheckConstraint("ck_run_steps_status", "status IN ('succeeded', 'failed', 'skipped')");
            });
            step.HasKey(s => new { s.RunId, s.Step, s.Name });
            step.Property(s => s.Name).HasMaxLength(63).HasDefaultValue("");
            step.Property(s => s.Step).HasConversion(LowercaseEnum<RunStepName>()).HasMaxLength(16);
            step.Property(s => s.Status).HasConversion(LowercaseEnum<RunStepStatus>()).HasMaxLength(16);
            step.Property(s => s.Tables).HasJsonColumn().HasDefaultValueSql("'[]'::jsonb");
            step.Property(s => s.Log).HasDefaultValue("");
        });

        modelBuilder.Entity<ScriptTest>(test =>
        {
            test.ToTable("script_tests", table =>
            {
                table.HasCheckConstraint("ck_script_tests_kind", "kind IN ('clean', 'output', 'indicator')");
                table.HasCheckConstraint("ck_script_tests_name", "(kind = 'indicator') = (name IS NOT NULL)");
                table.HasCheckConstraint("ck_script_tests_status", "status IN ('queued', 'running', 'succeeded', 'failed')");
                table.HasCheckConstraint("ck_script_tests_sample_size", "sample_size BETWEEN 1 AND 5000");
                table.HasCheckConstraint("ck_script_tests_stop_line", "stop_line > 0");
            });
            test.HasKey(t => t.Id);
            test.Property(t => t.Id).ValueGeneratedNever();
            test.Property(t => t.Kind).HasConversion(LowercaseEnum<ScriptKind>()).HasMaxLength(16);
            test.Property(t => t.Name).HasMaxLength(63);
            test.Property(t => t.Status).HasConversion(LowercaseEnum<RunStatus>()).HasMaxLength(16);
            test.Property(t => t.ErrorCode).HasMaxLength(64);
            test.Property(t => t.Steps).HasJsonColumn().HasDefaultValueSql("'[]'::jsonb");
            // At most one queued test per user and project; a new test replaces the queued one.
            test.HasIndex(t => new { t.ProjectId, t.RequestedBy }).IsUnique().HasFilter("status = 'queued'")
                .HasDatabaseName("ix_script_tests_project_id_requested_by_queued");
            test.HasIndex(t => new { t.Status, t.QueuedAt });
            test.HasIndex(t => t.FinishedAt);
            test.HasOne<Project>().WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);
            test.HasOne<User>().WithMany().HasForeignKey(t => t.RequestedBy).OnDelete(DeleteBehavior.Restrict);
            // NO ACTION: a project's deletion cascades to tests and script versions in the same statement.
            test.HasOne<ScriptVersion>().WithMany().HasForeignKey(t => t.CleanVersionId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<SampleDownload>(download =>
        {
            download.ToTable("sample_downloads", table =>
            {
                table.HasCheckConstraint("ck_sample_downloads_status", "status IN ('queued', 'running', 'succeeded', 'failed')");
                table.HasCheckConstraint("ck_sample_downloads_sample_size", "sample_size BETWEEN 1 AND 5000");
            });
            download.HasKey(d => d.Id);
            download.Property(d => d.Id).ValueGeneratedNever();
            download.Property(d => d.Status).HasConversion(LowercaseEnum<RunStatus>()).HasMaxLength(16);
            download.Property(d => d.FileSha256).HasMaxLength(64);
            download.Property(d => d.Contents).HasJsonObjectColumn();
            download.Property(d => d.ErrorCode).HasMaxLength(64);
            // At most one queued download per user and project; a new request replaces the queued one.
            download.HasIndex(d => new { d.ProjectId, d.RequestedBy }).IsUnique().HasFilter("status = 'queued'")
                .HasDatabaseName("ix_sample_downloads_project_id_requested_by_queued");
            download.HasIndex(d => new { d.Status, d.QueuedAt });
            download.HasIndex(d => d.FinishedAt);
            download.HasOne<Project>().WithMany().HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Cascade);
            download.HasOne<User>().WithMany().HasForeignKey(d => d.RequestedBy).OnDelete(DeleteBehavior.Restrict);
            // NO ACTION: a project's deletion cascades to downloads and script versions in the same statement.
            download.HasOne<ScriptVersion>().WithMany().HasForeignKey(d => d.CleanVersionId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<SampleDownloadPart>(part =>
        {
            part.ToTable("sample_download_parts");
            part.HasKey(p => new { p.DownloadId, p.Part });
            part.Property(p => p.Data).HasColumnType("bytea");
            part.HasOne<SampleDownload>().WithMany().HasForeignKey(p => p.DownloadId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssistantCall>(call =>
        {
            call.ToTable("assistant_calls", table =>
            {
                table.HasCheckConstraint("ck_assistant_calls_kind", "kind IN ('clean', 'output', 'indicator')");
                table.HasCheckConstraint("ck_assistant_calls_action", "action IN ('script', 'cell', 'fix')");
                table.HasCheckConstraint("ck_assistant_calls_status", "status IN ('running', 'succeeded', 'failed', 'cancelled')");
                table.HasCheckConstraint("ck_assistant_calls_outcome", "outcome IS NULL OR outcome IN ('script', 'cell', 'insert')");
            });
            call.HasKey(c => c.Id);
            call.Property(c => c.Id).ValueGeneratedNever();
            call.Property(c => c.Kind).HasConversion(LowercaseEnum<ScriptKind>()).HasMaxLength(16);
            call.Property(c => c.Name).HasMaxLength(63);
            call.Property(c => c.Action).HasConversion(LowercaseEnum<AssistantAction>()).HasMaxLength(16);
            call.Property(c => c.Model).HasMaxLength(200);
            call.Property(c => c.Context).HasJsonObjectColumn();
            call.Property(c => c.Status).HasConversion(LowercaseEnum<AssistantCallStatus>()).HasMaxLength(16);
            call.Property(c => c.ErrorCode).HasMaxLength(64);
            call.Property(c => c.TokensEstimated).HasDefaultValue(false);
            call.Property(c => c.Outcome).HasConversion(LowercaseEnum<AssistantOutcome>()).HasMaxLength(16);
            call.Property(c => c.StartedAt).HasDefaultValueSql("now()");
            // A user has at most one request in progress.
            call.HasIndex(c => c.UserId).IsUnique().HasFilter("status = 'running'").HasDatabaseName("ix_assistant_calls_user_id_running");
            // The daily budget sums a user's rows since the start of the UTC day.
            call.HasIndex(c => new { c.UserId, c.StartedAt });
            call.HasIndex(c => c.ProjectId);
            call.HasOne<Project>().WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
            call.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReferenceTable>(reference =>
        {
            reference.ToTable("reference_tables", table => table.HasCheckConstraint("ck_reference_tables_name", "name ~ '^[a-z][a-z0-9_]{0,62}$'"));
            reference.HasKey(r => r.Id);
            reference.Property(r => r.Id).ValueGeneratedNever();
            reference.Property(r => r.Name).HasMaxLength(63);
            reference.Property(r => r.FileName).HasMaxLength(255);
            reference.Property(r => r.Columns).HasJsonColumn();
            reference.HasIndex(r => new { r.ProjectId, r.Name }).IsUnique();
            reference.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
            reference.HasOne(r => r.UpdatedByUser).WithMany().HasForeignKey(r => r.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReferenceTableContent>(content =>
        {
            content.ToTable("reference_table_contents");
            content.HasKey(c => c.ReferenceTableId);
            content.Property(c => c.Rows).HasColumnType("jsonb");
            content.HasOne<ReferenceTable>().WithOne().HasForeignKey<ReferenceTableContent>(c => c.ReferenceTableId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FormVersion>(version =>
        {
            version.ToTable("form_versions", table => table.HasCheckConstraint("ck_form_versions_status", "status IN ('pending', 'available', 'failed', 'not_found')"));
            version.HasKey(v => new { v.ProjectId, v.Uid });
            version.Property(v => v.Uid).HasMaxLength(128);
            version.Property(v => v.Status).HasConversion(FormVersionStatusConverter()).HasMaxLength(16);
            version.Property(v => v.Fields).HasJsonColumn().HasDefaultValueSql("'[]'::jsonb");
            version.Property(v => v.Choices).HasJsonColumn().HasDefaultValueSql("'[]'::jsonb");
            version.Property(v => v.Attempts).HasDefaultValue(0);
            version.Property(v => v.ErrorCode).HasMaxLength(64);
            // The background pass looks for versions that are due.
            version.HasIndex(v => v.NextAttemptAt).HasFilter("status IN ('pending', 'failed')");
            version.HasOne<Project>().WithMany().HasForeignKey(v => v.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Dashboard>(dashboard =>
        {
            dashboard.ToTable("dashboards", table => table.HasCheckConstraint("ck_dashboards_position", "position >= 0"));
            dashboard.HasKey(d => d.Id);
            dashboard.Property(d => d.Id).HasDefaultValueSql("gen_random_uuid()");
            dashboard.Property(d => d.Name).HasColumnType("text");
            dashboard.Property(d => d.NormalizedName).HasColumnType("text");
            dashboard.Property(d => d.Filters).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
            dashboard.Property(d => d.CreatedAt).HasDefaultValueSql("now()");
            dashboard.Property(d => d.UpdatedAt).HasDefaultValueSql("now()");
            // One name per project whatever its case; also serves the lookups by project, so the project has no index of its own.
            dashboard.HasIndex(d => new { d.ProjectId, d.NormalizedName }).IsUnique();
            dashboard.HasOne<Project>().WithMany().HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Cascade);
            dashboard.HasOne<User>().WithMany().HasForeignKey(d => d.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            dashboard.HasOne<User>().WithMany().HasForeignKey(d => d.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DashboardWidget>(widget =>
        {
            widget.ToTable("dashboard_widgets", table => table.HasCheckConstraint(
                "ck_dashboard_widgets_chart_type", "chart_type IN ('stat', 'bar', 'column', 'line', 'histogram', 'pie', 'map')"));
            widget.HasKey(w => w.Id);
            widget.Property(w => w.Id).HasDefaultValueSql("gen_random_uuid()");
            widget.Property(w => w.ChartType).HasColumnType("text");
            widget.Property(w => w.Title).HasColumnType("text");
            widget.Property(w => w.Config).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            widget.Property(w => w.Layout).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            widget.Property(w => w.CreatedAt).HasDefaultValueSql("now()");
            widget.Property(w => w.UpdatedAt).HasDefaultValueSql("now()");
            widget.HasIndex(w => w.ProjectId);
            widget.HasIndex(w => w.DashboardId);
            widget.HasOne<Project>().WithMany().HasForeignKey(w => w.ProjectId).OnDelete(DeleteBehavior.Cascade);
            // Removing a dashboard removes its widgets; a project's deletion cascades to both tables in one statement.
            widget.HasOne<Dashboard>().WithMany().HasForeignKey(w => w.DashboardId).OnDelete(DeleteBehavior.Cascade);
            widget.HasOne<User>().WithMany().HasForeignKey(w => w.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            widget.HasOne<User>().WithMany().HasForeignKey(w => w.UpdatedBy).OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>Stores <see cref="FormVersionStatus"/> as the snake_case text of its name (<c>NotFound</c> → <c>not_found</c>).</summary>
    private static ValueConverter<FormVersionStatus, string> FormVersionStatusConverter() =>
        new(status => FormVersionStatusText.ToText(status), text => FormVersionStatusText.Parse(text));

    /// <summary>Stores an enum as lowercase text (<c>Admin</c> → <c>admin</c>).</summary>
    private static ValueConverter<TEnum, string> LowercaseEnum<TEnum>()
        where TEnum : struct, Enum =>
        new(value => value.ToString().ToLowerInvariant(), value => Enum.Parse<TEnum>(value, true));
}
