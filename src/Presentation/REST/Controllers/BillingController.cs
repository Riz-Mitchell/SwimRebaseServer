using System.Security.Cryptography;
using System.Text;
using Auth.Application.Interfaces;
using Billing.Application.Models;
using Billing.Application.Options;
using Billing.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using REST.Filters;

namespace REST.Controllers;

[ApiController]
[Route("api/billing")]
public sealed class BillingController(
    EntitlementService entitlements,
    RevenueCatWebhookHandler webhook,
    IOptions<RevenueCatOptions> revenueCat,
    ICurrentUserAccessor currentUser,
    ILogger<BillingController> logger) : ControllerBase
{
    /// <summary>The user's plan (trial, pro or free), their subscription if any and this week's conversion allowance.</summary>
    [HttpGet("entitlement")]
    [Authorize]
    [ETag]
    public async Task<IActionResult> GetEntitlement(CancellationToken cancellationToken) =>
        Ok(EntitlementResponse.From(await entitlements.GetAsync(currentUser.UserId, cancellationToken)));

    /// <summary>RevenueCat's webhook. Not a user endpoint: it is authenticated by the shared secret RevenueCat sends in the Authorization header.</summary>
    [HttpPost("revenuecat")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveRevenueCat([FromBody] RevenueCatWebhook body, CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
        {
            logger.LogWarning("[{ClassName}] Rejected RevenueCat webhook: missing or wrong shared secret", nameof(BillingController));
            return Unauthorized();
        }

        if (body.Event is { } e)
        {
            logger.LogInformation("[{ClassName}] Received RevenueCat event {EventId} ({Type})", nameof(BillingController), e.Id, e.Type);
            await webhook.HandleAsync(e, cancellationToken);
        }
        else
        {
            logger.LogWarning("[{ClassName}] RevenueCat webhook body had no event", nameof(BillingController));
        }

        return Ok();
    }

    private bool IsAuthorized()
    {
        var secret = revenueCat.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
        {
            logger.LogError("[{ClassName}] 'RevenueCat:WebhookSecret' is not configured; every webhook will be rejected", nameof(BillingController));
            return false;
        }

        var sent = Request.Headers.Authorization.ToString();
        if (sent.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            sent = sent["Bearer ".Length..];
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(secret));
    }
}

public sealed record EntitlementResponse(string Plan, DateTimeOffset? TrialEndsAt, SubscriptionView? Subscription, QuotaSnapshot Conversions)
{
    public static EntitlementResponse From(Entitlement e) => new(e.Plan.ToString().ToLowerInvariant(), e.TrialEndsAt, e.Subscription, e.Conversions);
}
