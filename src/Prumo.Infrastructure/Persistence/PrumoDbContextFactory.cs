using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Prumo.Infrastructure.Persistence;

public sealed class PrumoDbContextFactory : IDesignTimeDbContextFactory<PrumoDbContext>
{
    public PrumoDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=prumo;Username=prumo;Password=prumo";

        var options = new DbContextOptionsBuilder<PrumoDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PrumoDbContext(options);
    }
}
