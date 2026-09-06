using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without starting the Api project.
/// </summary>
/// <remarks>
/// The connection string below is never connected to: adding or scripting a
/// migration reads the model and not the database. Keeping the tooling out of
/// the composition root is what makes a migration something that can be added
/// without a running instance anywhere:
/// <c>dotnet ef migrations add Name --project src/Hostingaffe.Infrastructure</c>.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HostingaffeDbContext>
{
    public HostingaffeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HostingaffeDbContext>()
            .UseNpgsql("Host=design-time;Database=hostingaffe;Username=hostingaffe")
            .Options;

        return new HostingaffeDbContext(options);
    }
}
