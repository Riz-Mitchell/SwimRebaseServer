using Ai.Infrastructure;
using Auth.Infrastructure;
using Auth.Infrastructure.Middleware;
using Training.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddAiInfrastructure(builder.Configuration);
builder.Services.AddTrainingInfrastructure(builder.Configuration);

var app = builder.Build();

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
