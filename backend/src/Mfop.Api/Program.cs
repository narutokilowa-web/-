using Mfop.Access;
using Mfop.Access.Api;
using Mfop.Access.Infrastructure;
using Mfop.Api;
using Mfop.Assets;
using Mfop.Assets.Api;
using Mfop.Audit;
using Mfop.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("App")
    ?? throw new InvalidOperationException("ConnectionStrings:App is required (runtime role mfop_app).");

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddMfopAuthentication(builder.Configuration);
builder.Services.AddMfopPersistence(connectionString);
builder.Services.AddAuditModule();
builder.Services.AddAccessModule();
builder.Services.AddAssetsModule(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AccessDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseMiddleware<ActorResolutionMiddleware>();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapAccessEndpoints();
app.MapAssetEndpoints();

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
