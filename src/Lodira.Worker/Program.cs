using Lodira.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Lodira.Application.Abstractions;
using Lodira.Application.Documents;
using Lodira.Application.Intelligence;
using Lodira.Application.Contributions;
using Lodira.Application.PortfolioImports;
using Lodira.Infrastructure.Ai;
using Lodira.Infrastructure.DocumentSources;
using Lodira.Infrastructure.Documents;
using Lodira.Infrastructure.Persistence;
using Lodira.Infrastructure.Storage;
using Lodira.Infrastructure.Jobs;

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
        else if (string.Equals(aiProvider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<OpenAiAnalysisService>();
            services.AddScoped<IAiAnalysisService>(provider => new RateLimitedAiAnalysisService(
                provider.GetRequiredService<OpenAiAnalysisService>(),
                provider.GetRequiredService<IAiRequestRateLimiter>(),
                provider.GetRequiredService<ILogger<RateLimitedAiAnalysisService>>()));
        }
        else if (string.Equals(aiProvider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<GeminiAnalysisService>();
            services.AddScoped<IAiAnalysisService>(provider => new RateLimitedAiAnalysisService(
                provider.GetRequiredService<GeminiAnalysisService>(),
                provider.GetRequiredService<IAiRequestRateLimiter>(),
                provider.GetRequiredService<ILogger<RateLimitedAiAnalysisService>>()));
        }
        else
        {
            services.AddHttpClient<HttpAiAnalysisService>();
            services.AddScoped<IAiAnalysisService>(provider => new RateLimitedAiAnalysisService(
                provider.GetRequiredService<HttpAiAnalysisService>(),
                provider.GetRequiredService<IAiRequestRateLimiter>(),
                provider.GetRequiredService<ILogger<RateLimitedAiAnalysisService>>()));
        }

        services.AddDbContext<LodiraDbContext>(options => options.UseNpgsql(context.Configuration.GetConnectionString("Postgres")));
        services.AddScoped<IProcessingJobQueue, PostgresProcessingJobQueue>();
        services.Configure<CvmFnetOptions>(context.Configuration.GetSection("Documents"));
        services.Configure<DocumentBootstrapOptions>(context.Configuration.GetSection("DocumentBootstrap"));
        services.AddHttpClient<CvmFnetDocumentSource>();
        services.AddHttpClient("documents");
        services.Configure<JobProcessingOptions>(context.Configuration.GetSection("Jobs"));
        services.Configure<AiProviderOptions>(context.Configuration.GetSection("AI"));
        if (string.Equals(aiProvider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<GeminiContributionIntelligenceProvider>();
            services.AddScoped<IContributionIntelligenceProvider>(provider => provider.GetRequiredService<GeminiContributionIntelligenceProvider>());
        }
        else services.AddSingleton<IContributionIntelligenceProvider, DeterministicContributionIntelligenceProvider>();
        services.AddSingleton<IAiRequestRateLimiter, AiRequestRateLimiter>();
        services.Configure<TextQualityOptions>(context.Configuration.GetSection("TextQuality"));
        services.AddScoped<DocumentAnalysisProcessor>();
        services.AddSingleton<WorkerMetrics>();
        services.AddSingleton<AiAnalysisValidator>();
        services.AddSingleton<IPortfolioImpactAssessmentService, PortfolioImpactAssessmentService>();
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<ITextQualityValidator>(provider => new TextQualityValidator(provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TextQualityOptions>>().Value));
        services.AddScoped<IDocumentSource>(provider => provider.GetRequiredService<CvmFnetDocumentSource>());
        services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(context.Configuration["Storage:RootPath"] ?? "/storage"));
        services.AddHostedService<DocumentDiscoveryWorker>();
        services.AddHostedService<StorageMaintenanceWorker>();
        services.AddHostedService<RadarRepairWorker>();
        services.AddHostedService<DocumentDownloadWorker>();
        services.AddHostedService<DocumentAnalysisWorker>();
        services.AddHostedService<ContributionGuidanceWorker>();
    })
    .Build();

await host.RunAsync();
