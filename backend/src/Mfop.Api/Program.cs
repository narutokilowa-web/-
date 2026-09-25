using Mfop.Access;
using Mfop.Access.Api;
using Mfop.Access.Infrastructure;
using Mfop.Api;
using Mfop.Assets;
using Mfop.Assets.Api;
using Mfop.Audit;
using Mfop.Audit.Api;
using Mfop.Evidence;
using Mfop.Evidence.Api;
using Mfop.Faults;
using Mfop.Faults.Api;
using Mfop.Infrastructure;
using Mfop.Inventory;
using Mfop.Inventory.Api;
using Mfop.Rules;
using Mfop.Rules.Api;
using Mfop.Sync;
using Mfop.Sync.Api;
using Mfop.WorkOrders;
using Mfop.WorkOrders.Api;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("App")
    ?? throw new InvalidOperationException("ConnectionStrings:App is required (runtime role mfop_app).");

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddMfopAuthentication(builder.Configuration);
builder.Services.AddMfopPersistence(connectionString);
builder.Services.AddAuditModule();
builder.Services.AddAccessModule(builder.Configuration).AddAccessWorkers();
builder.Services.AddAssetsModule(builder.Configuration);
builder.Services.AddFaultsModule();
builder.Services.AddWorkOrdersModule(builder.Configuration);
builder.Services.AddRulesModule();
builder.Services.AddInventoryModule(builder.Configuration);
builder.Services.AddEvidenceModule(builder.Configuration);
builder.Services.AddSyncModule();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AccessDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseMiddleware<ActorResolutionMiddleware>();
app.UseMiddleware<DeviceTrustMiddleware>();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapAccessEndpoints();
app.MapAssetEndpoints();
app.MapFaultEndpoints();
app.MapWorkOrderEndpoints();
app.MapRulesEndpoints();
app.MapInventoryEndpoints();
app.MapEvidenceEndpoints();
app.MapAuditEndpoints();
app.MapSyncEndpoints();

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
