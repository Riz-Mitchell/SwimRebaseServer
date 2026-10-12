using Auth.Application.Exceptions;
using Auth.Application.Interfaces;
using Auth.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace REST.Controllers;

[ApiController]
[Authorize]
[Route("api/account")]
public sealed class AccountController(AccountDeletionService deletion, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpPost("deletion")]
    public IActionResult RequestDeletion()
    {
        try
        {
            var request = deletion.Request(currentUser.UserId, currentUser.LastSignInAt);
            return Ok(new DeletionRequestResponse(request.ConfirmationToken, request.ExpiresAt));
        }
        catch (RecentLoginRequiredException)
        {
            return ReauthenticationRequired();
        }
    }

    [HttpPost("deletion/confirm")]
    public async Task<IActionResult> ConfirmDeletion([FromBody] ConfirmDeletionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await deletion.ConfirmAsync(currentUser.UserId, request.ConfirmationToken, cancellationToken);
            return NoContent();
        }
        catch (InvalidDeletionConfirmationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private ObjectResult ReauthenticationRequired() =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = "reauthentication_required" });
}

public sealed record DeletionRequestResponse(string ConfirmationToken, DateTimeOffset ExpiresAt);

public sealed record ConfirmDeletionRequest(string ConfirmationToken);
