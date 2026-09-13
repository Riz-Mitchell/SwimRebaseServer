using System.Security.Claims;
using Ai.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Ai.Infrastructure.Auth;

/// <summary>
/// Resolves the Supabase user id from the `sub` claim of the already-validated JWT on the current
/// request. Relies on JWT Bearer auth being configured against Supabase's shared JWT secret
/// (see the REST host's startup) - this class only ever reads a principal ASP.NET has already
/// validated; it never parses or trusts a token itself.
/// </summary>
public sealed class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    public Guid UserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User
                ?? throw new InvalidOperationException("No active HTTP request to resolve the current user from.");

            var subject = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (subject is null)
            {
                throw new UnauthorizedAccessException("The request has no authenticated user (missing 'sub' claim).");
            }

            if (!Guid.TryParse(subject, out var userId))
            {
                throw new UnauthorizedAccessException($"The 'sub' claim '{subject}' is not a valid user id.");
            }

            return userId;
        }
    }
}
