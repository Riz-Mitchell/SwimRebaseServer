using Ai.Application.Interfaces;
using Ai.Application.Options;
using Ai.Application.Services;
using Ai.Infrastructure.Auth;
using Ai.Infrastructure.Chat;
using Ai.Infrastructure.Persistence;
using Ai.Infrastructure.Repositories;
using Ai.Infrastructure.Tools;
using Anthropic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ai.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the Ai feature's persistence, Anthropic client, and orchestration services.
    /// Expects a "AiDatabase" connection string and, optionally, "Anthropic:ApiKey" (falls back to
    /// the ANTHROPIC_API_KEY environment variable, same as the bare Anthropic SDK client).
    /// </summary>
    public static IServiceCollection AddAiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AiDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("AiDatabase"))
            .UseSnakeCaseNamingConvention());

        services.Configure<AiModelOptions>(configuration.GetSection(AiModelOptions.SectionName));
        services.Configure<ChatCompactionOptions>(configuration.GetSection(ChatCompactionOptions.SectionName));

        services.AddHttpContextAccessor();

        services.AddSingleton(_ =>
        {
            var apiKey = configuration["Anthropic:ApiKey"];
            return string.IsNullOrWhiteSpace(apiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = apiKey };
        });

        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IToolExecutionRepository, ToolExecutionRepository>();
        services.AddScoped<ITokenUsageRepository, TokenUsageRepository>();
        services.AddScoped<IAiToolRegistry, AiToolRegistry>();
        services.AddScoped<IAiChatClient, AnthropicChatClient>();
        services.AddScoped<IChatCompactionService, ChatCompactionService>();
        services.AddScoped<ToolExecutor>();
        services.AddScoped<GatedToolApprovalResolver>();
        services.AddScoped<ChatTurnLoop>();
        services.AddScoped<ChatOrchestrator>();

        return services;
    }
}
