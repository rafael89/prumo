using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Lodira.Infrastructure.Persistence;

public sealed class LodiraDbContextFactory : IDesignTimeDbContextFactory<LodiraDbContext>
{
    public LodiraDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=prumo;Username=postgres;Password=dev123";

        var options = new DbContextOptionsBuilder<LodiraDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new LodiraDbContext(options);
    }
}
