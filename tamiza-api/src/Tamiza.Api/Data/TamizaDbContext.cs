using Microsoft.EntityFrameworkCore;

namespace Tamiza.Api.Data;

public sealed class TamizaDbContext(DbContextOptions<TamizaDbContext> options) : DbContext(options)
{
    /// <summary>Schema that holds the metadata tables owned by the API.</summary>
    public const string Schema = "tamiza";

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).ValueGeneratedNever();
            user.Property(u => u.KeycloakSub).HasMaxLength(255);
            user.HasIndex(u => u.KeycloakSub).IsUnique();
            user.Property(u => u.Name).HasMaxLength(255);
            user.Property(u => u.Email).HasMaxLength(320);
        });
    }
}
