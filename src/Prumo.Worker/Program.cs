using Prumo.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Application.Documents;
using Prumo.Infrastructure.Ai;
using Prumo.Infrastructure.DocumentSources;
using Prumo.Infrastructure.Documents;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Storage;
using Prumo.Infrastructure.Jobs;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var aiProvider = context.Configuration["AI:Provider"]?.Trim();
        if (string.Equals(aiProvider, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            if (context.HostingEnvironment.IsProduction())
            {
                throw new InvalidOperationException("AI:Provider=Fake não é permitido em Production.");
            }

            services.AddScoped<IAiAnalysisService, FakeAiAnalysisService>();
        }
        else
        {
            services.AddHttpClient<IAiAnalysisService, HttpAiAnalysisService>();
        }

        services.AddDbContext<PrumoDbContext>(options => options.UseNpgsql(context.Configuration.GetConnectionString("Postgres")));
        services.AddScoped<IProcessingJobQueue, PostgresProcessingJobQueue>();
        services.Configure<CvmFnetOptions>(context.Configuration.GetSection("Documents"));
        services.AddHttpClient<CvmFnetDocumentSource>();
        services.AddHttpClient("documents");
        services.Configure<JobProcessingOptions>(context.Configuration.GetSection("Jobs"));
        services.Configure<AiProviderOptions>(context.Configuration.GetSection("AI"));
        services.Configure<TextQualityOptions>(context.Configuration.GetSection("TextQuality"));
        services.AddScoped<DocumentAnalysisProcessor>();
        services.AddSingleton<AiAnalysisValidator>();
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<ITextQualityValidator>(provider => new TextQualityValidator(provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TextQualityOptions>>().Value));
        services.AddScoped<IDocumentSource>(provider => provider.GetRequiredService<CvmFnetDocumentSource>());
        services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(context.Configuration["Storage:RootPath"] ?? "/storage"));
        services.AddHostedService<DocumentDiscoveryWorker>();
        services.AddHostedService<DocumentDownloadWorker>();
        services.AddHostedService<DocumentAnalysisWorker>();
    })
    .Build();

await host.RunAsync();
