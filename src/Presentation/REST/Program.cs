using Ai.Infrastructure;
using Auth.Infrastructure;
using Auth.Infrastructure.Middleware;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Training.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// In Azure, KeyVaultUri is set once by infra/main.bicep - pulls every secret straight into
// IConfiguration via the web app's managed identity. Adding a new secret is then just
// `az keyvault secret set`; no app settings or workflow changes needed. Absent locally, so
// local dev relies on appsettings.json / user-secrets as usual.
var keyVaultUri = builder.Configuration["KeyVaultUri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddAiInfrastructure(builder.Configuration);
builder.Services.AddTrainingInfrastructure(builder.Configuration);

var app = builder.Build();

// Each feature owns its own migrations against the shared "Database" connection string - apply
// them all on startup so a fresh/wiped database is brought up to date without a manual
// `dotnet ef database update` step per feature.
using (var migrationScope = app.Services.CreateScope())
{
    await migrationScope.ServiceProvider.GetRequiredService<AiDbContext>().Database.MigrateAsync();
    await migrationScope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
    await migrationScope.ServiceProvider.GetRequiredService<TrainingDbContext>().Database.MigrateAsync();
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
