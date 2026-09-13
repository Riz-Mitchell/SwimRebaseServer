using System.Text;
using Ai.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAiInfrastructure(builder.Configuration);

var supabaseJwtSecret = builder.Configuration["Supabase:JwtSecret"];
if (string.IsNullOrWhiteSpace(supabaseJwtSecret))
{
    throw new InvalidOperationException(
        "Missing configuration 'Supabase:JwtSecret' - set it via user-secrets or an environment " +
        "variable to the project's JWT secret (Supabase dashboard: Project Settings > API > JWT Settings).");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the raw 'sub' claim instead of letting the handler remap it onto ClaimTypes.NameIdentifier,
        // so ICurrentUserAccessor reads the same claim name Supabase issues.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabaseJwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true,
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
