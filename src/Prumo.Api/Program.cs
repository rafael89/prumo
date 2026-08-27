using Microsoft.EntityFrameworkCore;
using Prumo.Application.Abstractions;
using Prumo.Infrastructure.DocumentSources;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
    policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddDbContext<PrumoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.Configure<CvmFnetOptions>(builder.Configuration.GetSection("Documents"));
builder.Services.AddHttpClient<CvmFnetDocumentSource>();
builder.Services.AddScoped<IDocumentSource>(provider => provider.GetRequiredService<CvmFnetDocumentSource>());
builder.Services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(builder.Configuration["Storage:RootPath"] ?? "/storage"));
builder.Services.AddHealthChecks();

var app = builder.Build();

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
