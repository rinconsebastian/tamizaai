using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tamiza.Api.Data;

/// <summary>Lets <c>dotnet ef</c> build the model without the web host or a reachable database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TamizaDbContext>
{
    public TamizaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TamizaDbContext>();
        DatabaseRegistration.Configure(options, "Host=localhost;Database=tamiza;Username=tamiza");
        return new TamizaDbContext(options.Options);
    }
}
