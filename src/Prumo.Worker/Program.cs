using Prumo.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Infrastructure.DocumentSources;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Storage;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddDbContext<PrumoDbContext>(options => options.UseNpgsql(context.Configuration.GetConnectionString("Postgres")));
        services.Configure<CvmFnetOptions>(context.Configuration.GetSection("Documents"));
        services.AddHttpClient<CvmFnetDocumentSource>();
        services.AddHttpClient("documents");
        services.AddScoped<IDocumentSource>(provider => provider.GetRequiredService<CvmFnetDocumentSource>());
        services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(context.Configuration["Storage:RootPath"] ?? "/storage"));
        services.AddHostedService<DocumentDiscoveryWorker>();
    })
    .Build();

await host.RunAsync();
