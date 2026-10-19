using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json;
using Prumo.Application.Abstractions;
using Prumo.Infrastructure.DocumentSources;
using Prumo.Infrastructure.Documents;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Storage;
using Prumo.Infrastructure.Notifications;
using Prumo.Application.PortfolioImports;
using Prumo.Application.Portfolio;
using Prumo.Infrastructure.PortfolioImports;
using Prumo.Application.Intelligence;
using Prumo.Api.Services;
using Prumo.Api;
using Prumo.Infrastructure.MarketData;
using Prumo.Infrastructure.Performance;
using Prumo.Infrastructure.Intelligence;
using Prumo.Infrastructure.Historical;
using Prumo.Infrastructure.Reconciliation;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddProvider(new ApiFileLoggerProvider(ApiFileLoggerProvider.ResolveDirectory(
    builder.Configuration["Storage:RootPath"] ?? "/storage", builder.Configuration["Logging:File:Path"])));

// GlobalExceptionHandler records the exception with the request path. The
// framework middleware would otherwise emit the same exception a second time.
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);

builder.Services.AddControllers();
builder.Services.AddScoped<PortfolioHistoryService>();
builder.Services.AddScoped<DividendForecastService>();
builder.Services.AddScoped<MarketDataService>();
builder.Services.AddScoped<ExternalAssetMaterializer>();
builder.Services.AddScoped<AssetMetadataCatalogSyncService>();
builder.Services.AddScoped<PortfolioPerformanceService>();
builder.Services.AddScoped<PortfolioIntelligenceService>();
builder.Services.AddScoped<HistoricalAnchorService>();
builder.Services.AddScoped<CoverageService>();
builder.Services.AddScoped<TransactionReconciliationService>();
builder.Services.AddScoped<HistoricalFinancialStateService>();
builder.Services.AddScoped<HistoricalPerformanceService>();
builder.Services.AddScoped<CorporateActionService>();
builder.Services.AddScoped<HistoricalCashStateService>();
builder.Services.AddScoped<HistoricalCompletenessService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
    policy.WithOrigins("http://localhost:5273").AllowAnyHeader().AllowAnyMethod()));
 builder.Services.AddDbContext<PrumoDbContext>(options =>
     options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"), npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection("Discord"));
builder.Services.AddHttpClient("DiscordWebhook");
builder.Services.AddSingleton<DiscordLogNotificationService>();
builder.Services.AddSingleton<IDiscordLogNotifier>(sp => sp.GetRequiredService<DiscordLogNotificationService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordLogNotificationService>());
builder.Logging.Services.AddSingleton<ILoggerProvider>(sp => new DiscordLogLoggerProvider(sp.GetRequiredService<IDiscordLogNotifier>(), sp.GetRequiredService<IOptions<DiscordOptions>>(), "API"));
builder.Services.Configure<CvmFnetOptions>(builder.Configuration.GetSection("Documents"));
builder.Services.Configure<DocumentBootstrapOptions>(builder.Configuration.GetSection("DocumentBootstrap"));
builder.Services.AddHttpClient<CvmFnetDocumentSource>();
builder.Services.AddSingleton<IPortfolioImporter, B3XlsxPortfolioImporter>();
builder.Services.AddScoped<NewAssetBootstrapService>();
builder.Services.AddScoped<IDocumentSource>(provider => provider.GetRequiredService<CvmFnetDocumentSource>());
builder.Services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(builder.Configuration["Storage:RootPath"] ?? "/storage"));
builder.Services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
builder.Services.AddMemoryCache();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgres", timeout: TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<IPortfolioAssessmentFreshnessEvaluator, PortfolioAssessmentFreshnessEvaluator>();
builder.Services.AddSingleton<IPortfolioImpactAssessmentService, PortfolioImpactAssessmentService>();
builder.Services.AddScoped<PortfolioAssessmentCurrentService>();
builder.Services.AddScoped<ContributionContextBuilder>();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<PrumoDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigration");

    logger.LogInformation("Aplicando migrations do banco de dados.");
    await db.Database.MigrateAsync();
    logger.LogInformation("Migrations do banco de dados aplicadas.");
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");
    if (exception is not null) logger.LogError(exception, "Erro inesperado ao processar {Method} {Path}", context.Request.Method, context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/problem+json";
    await JsonSerializer.SerializeAsync(context.Response.Body, new
    {
        type = "https://httpstatuses.com/500",
        title = "Erro interno",
        status = 500,
        detail = "Não foi possível concluir a operação. Tente novamente."
    });
}));

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
// TLS is terminated by the reverse proxy in container deployments. The proxy
// is responsible for redirecting external HTTP requests to HTTPS.
app.UseCors("frontend");
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.Run();

public partial class Program
{
}
