using Ai.Application;
using Ai.Application.Interfaces;
using Ai.Application.Options;
using Ai.Application.Services;
using Ai.Application.Services.ToolInteractions;
using Ai.Infrastructure.Chat;
using Ai.Infrastructure.Repositories;
using Ai.Infrastructure.SubAgents;
using Ai.Infrastructure.Tools;
using Anthropic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;
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
        services.Configure<AiTitleOptions>(configuration.GetSection(AiTitleOptions.SectionName));

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
        services.AddScoped<IUploadedImageRepository, UploadedImageRepository>();
        services.AddScoped<IAiToolRegistry, AiToolRegistry>();
        services.AddAiTool<GetTimetableTool>();
        services.AddAiTool<UpsertTimetableSlotTool>();
        services.AddAiTool<DeleteTimetableSlotTool>();
        services.AddAiTool<CreateFocusTool>();
        services.AddAiTool<EditFocusTool>();
        services.AddAiTool<DeleteFocusTool>();
        services.AddAiTool<AskUserQuestionTool>();
        services.AddAiTool<ReadSwimBoardTool>();
        services.AddAiTool<AskSwimQuestionsTool>();
        services.AddAiTool<EditDraftTool>();
        services.AddAiTool<SaveSessionTool>();
        services.AddAiTool<GetTrainingMetricsTool>();
        services.AddScoped<ISubAgent, SwimBoardInterviewAgent>();
        services.AddScoped<IConversationDriver, SwimBoardConversionDriver>();
        services.AddScoped<IAiChatClient, AnthropicChatClient>();
        services.AddScoped<IChatCompactionService, ChatCompactionService>();
        services.AddScoped<IConversationTitler, AiConversationTitler>();
        services.AddScoped<IAiUserProvisioningService, AiUserProvisioningService>();
        services.AddScoped<ImageUploadService>();
        services.AddScoped<ConversationManagementService>();
        services.AddScoped<ToolExecutor>();
        services.AddScoped<ToolTurnCloser>();
        services.AddScoped<SubAgentFollowUp>();
        services.AddScoped<ConversationRunner>();
        services.AddScoped<IAgentProfileProvider, AgentProfileProvider>();
        services.AddScoped<IToolInteraction, ApprovalInteraction>();
        services.AddScoped<IToolInteraction, QuestionInteraction>();
        services.AddScoped<IToolInteraction, SubAgentInteraction>();
        services.AddScoped<GatedToolApprovalResolver>();
        services.AddScoped<ChatTurnLoop>();
        services.AddScoped<ChatOrchestrator>();

        return services;
    }

    /// <summary>Registers a tool under both its own type and <see cref="IAiToolDefinition"/> (what the registry aggregates), resolving to the same scoped instance.</summary>
    private static IServiceCollection AddAiTool<TTool>(this IServiceCollection services) where TTool : class, IAiToolDefinition
    {
        services.AddScoped<TTool>();
        services.AddScoped<IAiToolDefinition>(sp => sp.GetRequiredService<TTool>());
        return services;
    }
}
