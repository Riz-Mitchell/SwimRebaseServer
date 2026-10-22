using Ai.Infrastructure;
using Analytics.Infrastructure;
using Auth.Infrastructure;
using Auth.Infrastructure.Middleware;
using Billing.Infrastructure;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Profile.Infrastructure;
using SessionConversion.Infrastructure;
using Sessions.Infrastructure;
using Shared.Infrastructure;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// In Azure, KeyVaultUri is set once by infra/main.bicep - pulls every secret straight into
// IConfiguration via the web app's managed identity. Adding a new secret is then just
// `az keyvault secret set`; no app settings or workflow changes needed. Absent locally, so
// local dev relies on appsettings.json / user-secrets as usual.
var keyVaultUri = builder.Configuration["KeyVaultUri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    Console.WriteLine($"[startup] Fetching secrets from Key Vault {keyVaultUri} ...");
    Console.Out.Flush();
    try
    {
        builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
        Console.WriteLine("[startup] Key Vault secrets loaded.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[startup] Key Vault fetch FAILED: {ex}");
        Console.Out.Flush();
        throw;
    }
    Console.Out.Flush();
}

// Add services to the container.

// Connection string comes from Key Vault in Azure; left empty locally so telemetry is simply off.
var appInsightsConnectionString = builder.Configuration.GetConnectionString("AppInsights");
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
    {
        options.ConnectionString = appInsightsConnectionString;
        options.TracesPerSecond = null;
        options.SamplingRatio = 1.0f;
    });
}

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSharedInfrastructure(builder.Configuration);
builder.Services.AddAnalyticsInfrastructure(builder.Configuration);
builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProfileInfrastructure(builder.Configuration);
builder.Services.AddBillingInfrastructure(builder.Configuration);
builder.Services.AddAiInfrastructure(builder.Configuration);
builder.Services.AddSessionsInfrastructure(builder.Configuration);
builder.Services.AddSessionConversionInfrastructure(builder.Configuration);

var app = builder.Build();

// Each feature owns its own migrations against the shared "Database" connection string - apply
// them all on startup so a fresh/wiped database is brought up to date without a manual
// `dotnet ef database update` step per feature.
using (var migrationScope = app.Services.CreateScope())
{
    foreach (var (name, migrate) in new (string, Func<Task>)[]
    {
        ("Ai", () => migrationScope.ServiceProvider.GetRequiredService<AiDbContext>().Database.MigrateAsync()),
        ("Analytics", () => migrationScope.ServiceProvider.GetRequiredService<AnalyticsDbContext>().Database.MigrateAsync()),
        ("Auth", () => migrationScope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync()),
        ("Billing", () => migrationScope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.MigrateAsync()),
        ("Profile", () => migrationScope.ServiceProvider.GetRequiredService<ProfileDbContext>().Database.MigrateAsync()),
        ("Sessions", () => migrationScope.ServiceProvider.GetRequiredService<SessionsDbContext>().Database.MigrateAsync()),
        ("SessionConversion", () => migrationScope.ServiceProvider.GetRequiredService<SessionConversionDbContext>().Database.MigrateAsync()),
    })
    {
        Console.WriteLine($"[startup] Migrating {name}Db ...");
        Console.Out.Flush();
        try
        {
            await migrate();
            Console.WriteLine($"[startup] {name}Db migrated.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[startup] {name}Db migration FAILED: {ex}");
            Console.Out.Flush();
            throw;
        }
        Console.Out.Flush();
    }
}

// Re-map effort saved before intensity became a percent. Idempotent; turn off with Sessions:BackfillIntensity=false once it has run everywhere.
if (app.Configuration.GetValue("Sessions:BackfillIntensity", true))
{
    using var backfillScope = app.Services.CreateScope();
    await backfillScope.ServiceProvider.GetRequiredService<IntensityBackfill>().RunAsync(CancellationToken.None);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseMiddleware<UserProvisioningMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();
