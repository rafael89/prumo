using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;
using System.Text.Json;
using Prumo.Application.Abstractions;
using Prumo.Infrastructure.DocumentSources;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Storage;
using Prumo.Infrastructure.Notifications;
using Prumo.Application.PortfolioImports;
using Prumo.Application.Portfolio;
using Prumo.Infrastructure.PortfolioImports;
using Prumo.Application.Intelligence;
using Prumo.Api.Services;
using Prumo.Api;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddScoped<PortfolioHistoryService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
    policy.WithOrigins("http://localhost:5273").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddDbContext<PrumoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection("Discord"));
builder.Services.AddHttpClient("DiscordWebhook");
builder.Services.AddSingleton<DiscordLogNotificationService>();
builder.Services.AddSingleton<IDiscordLogNotifier>(sp => sp.GetRequiredService<DiscordLogNotificationService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordLogNotificationService>());
builder.Logging.Services.AddSingleton<ILoggerProvider>(sp => new DiscordLogLoggerProvider(sp.GetRequiredService<IDiscordLogNotifier>(), sp.GetRequiredService<IOptions<DiscordOptions>>(), "API"));
builder.Services.Configure<CvmFnetOptions>(builder.Configuration.GetSection("Documents"));
builder.Services.Configure<DocumentBootstrapOptions>(builder.Configuration.GetSection("DocumentBootstrap"));
builder.Services.Configure<DocumentRetentionOptions>(builder.Configuration.GetSection("DocumentRetention"));
builder.Services.AddHttpClient<CvmFnetDocumentSource>();
builder.Services.AddSingleton<IPortfolioImporter, B3XlsxPortfolioImporter>();
builder.Services.AddScoped<NewAssetBootstrapService>();
builder.Services.AddScoped<IDocumentSource>(provider => provider.GetRequiredService<CvmFnetDocumentSource>());
builder.Services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(builder.Configuration["Storage:RootPath"] ?? "/storage"));
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IPortfolioAssessmentFreshnessEvaluator, PortfolioAssessmentFreshnessEvaluator>();
builder.Services.AddSingleton<IPortfolioImpactAssessmentService, PortfolioImpactAssessmentService>();
builder.Services.AddScoped<PortfolioAssessmentCurrentService>();

var app = builder.Build();

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

app.Run();

public partial class Program
{
}
