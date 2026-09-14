using Ai.Application;
using Ai.Application.Interfaces;
using Ai.Application.Options;
using Ai.Application.Services;
using Ai.Infrastructure.Chat;
using Ai.Infrastructure.Repositories;
using Ai.Infrastructure.Tools;
using Anthropic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure;

namespace Ai.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires the Ai feature's persistence, Anthropic client, and orchestration services.
    /// Expects a "Database" connection string (shared with the other features until they need to
    /// scale onto separate databases) and, optionally, "Anthropic:ApiKey" (falls back to the
    /// ANTHROPIC_API_KEY environment variable, same as the bare Anthropic SDK client).
    /// </summary>
    public static IServiceCollection AddAiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AiDbContext>(options => options
            .UseNpgsql(configuration.GetConnectionString("Database"))
            .UseSnakeCaseNamingConvention());

        services.Configure<AiModelOptions>(configuration.GetSection(AiModelOptions.SectionName));
        services.Configure<ChatCompactionOptions>(configuration.GetSection(ChatCompactionOptions.SectionName));

        services.AddSingleton(_ =>
        {
            var apiKey = configuration["Anthropic:ApiKey"];
            return string.IsNullOrWhiteSpace(apiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = apiKey };
        });

        // ICurrentUserAccessor is registered by Auth.Infrastructure.AddAuthInfrastructure - the Ai
        // feature only depends on the interface (via Auth.Application), never its implementation.
        services.AddKeyedScoped<IUnitOfWork, UnitOfWork<AiDbContext>>(AiKeys.UnitOfWork);
        services.AddScoped<IAiUserRepository, AiUserRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IToolExecutionRepository, ToolExecutionRepository>();
        services.AddScoped<ITokenUsageRepository, TokenUsageRepository>();
        services.AddScoped<IAiToolRegistry, AiToolRegistry>();
        services.AddScoped<IAiTool, GetTimetableTool>();
        services.AddScoped<IAiTool, UpsertTimetableSlotTool>();
        services.AddScoped<IAiTool, DeleteTimetableSlotTool>();
        services.AddScoped<IAiTool, CreateFocusTool>();
        services.AddScoped<IAiChatClient, AnthropicChatClient>();
        services.AddScoped<IChatCompactionService, ChatCompactionService>();
        services.AddScoped<IAiUserProvisioningService, AiUserProvisioningService>();
        services.AddScoped<ToolExecutor>();
        services.AddScoped<GatedToolApprovalResolver>();
        services.AddScoped<ChatTurnLoop>();
        services.AddScoped<ChatOrchestrator>();

        return services;
    }
}
