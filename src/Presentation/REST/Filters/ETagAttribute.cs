using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace REST.Filters;

/// <summary>
/// Tags a successful JSON response with a hash of its body and answers a matching <c>If-None-Match</c> with <c>304 Not Modified</c>.
/// The action still runs, so this saves the body, not the work.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ETagAttribute : ActionFilterAttribute
{
    private static readonly JsonSerializerOptions HashOptions = new(JsonSerializerDefaults.Web);

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is not OkObjectResult { Value: { } body })
        {
            return;
        }

        var etag = new EntityTagHeaderValue($"\"{Convert.ToBase64String(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(body, HashOptions)))}\"");
        var response = context.HttpContext.Response;
        response.Headers.ETag = etag.ToString();
        response.Headers.CacheControl = "private, no-cache";

        var sent = context.HttpContext.Request.GetTypedHeaders().IfNoneMatch;
        if (sent.Any(candidate => candidate.Equals(EntityTagHeaderValue.Any) || candidate.Compare(etag, useStrongComparison: false)))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
        }
    }
}
