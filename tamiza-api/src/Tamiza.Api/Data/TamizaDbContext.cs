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

    public DbSet<SamplingFrame> SamplingFrames => Set<SamplingFrame>();

    public DbSet<RawSubmission> RawSubmissions => Set<RawSubmission>();

    public DbSet<Sync> Syncs => Set<Sync>();

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
            project.Property(p => p.SyncIntervalHours).HasDefaultValue(12);
            project.Property(p => p.ReconcileIntervalHours).HasDefaultValue(24);
            project.Property(p => p.NextSyncAt).HasDefaultValueSql("now()");
            project.Property(p => p.NextReconcileAt).HasDefaultValueSql("(now() + interval '1 day')");
            project.ToTable(table =>
            {
                table.HasCheckConstraint("ck_projects_sync_interval_hours", "sync_interval_hours BETWEEN 1 AND 168");
                table.HasCheckConstraint("ck_projects_reconcile_interval_hours", "reconcile_interval_hours BETWEEN 1 AND 168");
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
    }

    /// <summary>Stores an enum as lowercase text (<c>Admin</c> → <c>admin</c>).</summary>
    private static ValueConverter<TEnum, string> LowercaseEnum<TEnum>()
        where TEnum : struct, Enum =>
        new(value => value.ToString().ToLowerInvariant(), value => Enum.Parse<TEnum>(value, true));
}
