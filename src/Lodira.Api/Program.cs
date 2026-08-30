using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;
using System.Text.Json;
using Lodira.Application.Abstractions;
using Lodira.Infrastructure.DocumentSources;
using Lodira.Infrastructure.Persistence;
using Lodira.Infrastructure.Storage;
using Lodira.Application.PortfolioImports;
using Lodira.Application.Portfolio;
using Lodira.Infrastructure.PortfolioImports;
using Lodira.Application.Intelligence;
using Lodira.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
    policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddDbContext<LodiraDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.Configure<CvmFnetOptions>(builder.Configuration.GetSection("Documents"));
builder.Services.Configure<DocumentBootstrapOptions>(builder.Configuration.GetSection("DocumentBootstrap"));
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
else
{
    app.UseHttpsRedirection();
}
app.UseCors("frontend");
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program
{
}
