using Microsoft.Extensions.Options;
using Prumo.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Application.Documents;
using Prumo.Application.Intelligence;
using Prumo.Application.Contributions;
using Prumo.Application.PortfolioImports;
using Prumo.Infrastructure.Ai;
using Prumo.Infrastructure.DocumentSources;
using Prumo.Infrastructure.Documents;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Storage;
using Prumo.Infrastructure.Jobs;
using Prumo.Infrastructure.Notifications;
using Prumo.Infrastructure.MarketData;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging((context, logging) =>
    {
        logging.Services.AddSingleton<ILoggerProvider>(sp => new DiscordLogLoggerProvider(sp.GetRequiredService<IDiscordLogNotifier>(), sp.GetRequiredService<IOptions<DiscordOptions>>(), "Worker"));
    })
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

        services.Configure<DiscordOptions>(context.Configuration.GetSection("Discord"));
        services.AddHttpClient("DiscordWebhook");
        services.AddSingleton<DiscordLogNotificationService>();
        services.AddSingleton<IDiscordLogNotifier>(sp => sp.GetRequiredService<DiscordLogNotificationService>());
        services.AddHostedService(sp => sp.GetRequiredService<DiscordLogNotificationService>());
        services.AddDbContext<PrumoDbContext>(options => options.UseNpgsql(context.Configuration.GetConnectionString("Postgres"), npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<MarketDataService>();
        services.AddScoped<ExternalAssetMaterializer>();
        services.AddScoped<IProcessingJobQueue, PostgresProcessingJobQueue>();
        services.Configure<CvmFnetOptions>(context.Configuration.GetSection("Documents"));
        services.Configure<DocumentBootstrapOptions>(context.Configuration.GetSection("DocumentBootstrap"));
        services.Configure<DocumentRetentionOptions>(context.Configuration.GetSection("DocumentRetention"));
        services.AddHttpClient<CvmFnetDocumentSource>();
        services.AddHttpClient<CvmFiiMonthlyStructuredSource>();
        services.AddScoped<CvmFiiMonthlyIngestionService>();
        services.Configure<CvmFiiIngestionOptions>(context.Configuration.GetSection("CvmFiiIngestion"));
        services.AddHttpClient("documents");
        services.Configure<JobProcessingOptions>(context.Configuration.GetSection("Jobs"));
        services.Configure<AiProviderOptions>(context.Configuration.GetSection("AI"));
        if (string.Equals(aiProvider, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<GeminiContributionIntelligenceProvider>();
            services.AddScoped<IContributionIntelligenceProvider>(provider => provider.GetRequiredService<GeminiContributionIntelligenceProvider>());
        }
        else
        {
            services.AddHttpClient<HttpContributionIntelligenceProvider>();
            services.AddScoped<IContributionIntelligenceProvider>(provider => provider.GetRequiredService<HttpContributionIntelligenceProvider>());
        }
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
        services.AddHostedService<CvmFiiMonthlyIngestionWorker>();
        services.AddHostedService<DocumentStorageRepairWorker>();
        services.AddHostedService<StorageMaintenanceWorker>();
        services.AddHostedService<RadarRepairWorker>();
        services.AddHostedService<DocumentDownloadWorker>();
        services.AddHostedService<DocumentAnalysisWorker>();
        services.AddHostedService<ContributionGuidanceWorker>();
    })
    .Build();

await host.RunAsync();
