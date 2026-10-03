using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tamiza.Api.Data;

public sealed class TamizaDbContext(DbContextOptions<TamizaDbContext> options) : DbContext(options)
{
    /// <summary>Schema that holds the metadata tables owned by the API.</summary>
    public const string Schema = "tamiza";

    private const string RoleCheck = "role IN ('admin', 'analyst', 'viewer')";

    public DbSet<User> Users => Set<User>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<ProjectInvitation> ProjectInvitations => Set<ProjectInvitation>();

    public DbSet<SamplingFrame> SamplingFrames => Set<SamplingFrame>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        var roleConverter = new ValueConverter<ProjectRole, string>(
            role => role.ToString().ToLowerInvariant(),
            value => Enum.Parse<ProjectRole>(value, true));

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
    }
}
