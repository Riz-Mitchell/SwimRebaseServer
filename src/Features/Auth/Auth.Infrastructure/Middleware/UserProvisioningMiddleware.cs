using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Auth.Infrastructure.Middleware;

/// <summary>
/// Runs after authentication on every request: if the caller has a valid Supabase JWT, ensures a
/// local <c>AppUser</c> row exists for it (creating one on first sight, else just touching
/// <c>LastSeenAt</c>). Unauthenticated requests pass through untouched - <c>[Authorize]</c> on
/// individual controllers still decides whether the request is allowed to proceed. A caller whose account was just
/// deleted is turned away so their remaining valid token cannot recreate it.
/// </summary>
public sealed class UserProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IUserProvisioningService provisioning, IDeletedUserList deletedUsers)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var subject = context.User.FindFirst("sub")?.Value;
            if (subject is not null && Guid.TryParse(subject, out var userId))
            {
                if (deletedUsers.Contains(userId))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var email = context.User.FindFirst("email")?.Value;
                await provisioning.EnsureUserExistsAsync(userId, email, context.RequestAborted);
            }
        }

        await next(context);
    }
}
