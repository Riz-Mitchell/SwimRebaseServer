using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Profile.Application.Services;

namespace REST.Filters;

/// <summary>
/// Refuses the request with <c>403 { code: "ai_consent_required" }</c> unless the user has accepted that their
/// photos and chat messages are sent to the AI provider. Apply to every action that can reach the provider.
/// Runs before the action starts, so streaming endpoints get a plain JSON 403 rather than a broken event stream.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireAiConsentAttribute : Attribute, IAsyncActionFilter
{
    public const string Code = "ai_consent_required";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var userId = services.GetRequiredService<ICurrentUserAccessor>().UserId;
        var onboarding = services.GetRequiredService<OnboardingService>();

        if (!await onboarding.HasAiConsentAsync(userId, context.HttpContext.RequestAborted))
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger<RequireAiConsentAttribute>()
                .LogWarning("[{ClassName}] Refused AI request from user {UserId}: no AI consent on record", nameof(RequireAiConsentAttribute), userId);
            context.Result = new ObjectResult(new { error = "AI consent required", code = Code })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
