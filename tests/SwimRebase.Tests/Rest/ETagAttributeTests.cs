using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using REST.Filters;

namespace SwimRebase.Tests.Rest;

public class ETagAttributeTests
{
    private static ActionExecutedContext Run(object body, string? ifNoneMatch = null)
    {
        var http = new DefaultHttpContext();
        if (ifNoneMatch is not null)
        {
            http.Request.Headers.IfNoneMatch = ifNoneMatch;
        }

        var context = new ActionExecutedContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), [], controller: new object())
        {
            Result = new OkObjectResult(body),
        };
        new ETagAttribute().OnActionExecuted(context);
        return context;
    }

    private static string TagOf(object body) => Run(body).HttpContext.Response.Headers.ETag.ToString();

    [Fact]
    public void A_response_is_tagged_and_kept_private()
    {
        var context = Run(new { plan = "free" });

        Assert.IsType<OkObjectResult>(context.Result);
        Assert.False(string.IsNullOrEmpty(context.HttpContext.Response.Headers.ETag));
        Assert.Equal("private, no-cache", context.HttpContext.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void The_same_body_gets_the_same_tag_and_a_changed_body_a_different_one()
    {
        Assert.Equal(TagOf(new { plan = "free", used = 1 }), TagOf(new { plan = "free", used = 1 }));
        Assert.NotEqual(TagOf(new { plan = "free", used = 1 }), TagOf(new { plan = "free", used = 2 }));
    }

    [Fact]
    public void A_matching_if_none_match_is_answered_with_304()
    {
        var tag = TagOf(new { plan = "free" });

        var context = Run(new { plan = "free" }, tag);

        Assert.Equal(StatusCodes.Status304NotModified, Assert.IsType<StatusCodeResult>(context.Result).StatusCode);
        Assert.Equal(tag, context.HttpContext.Response.Headers.ETag.ToString());
    }

    [Fact]
    public void A_stale_if_none_match_gets_the_full_response()
    {
        var context = Run(new { plan = "pro" }, TagOf(new { plan = "free" }));

        Assert.IsType<OkObjectResult>(context.Result);
    }

    [Fact]
    public void Error_results_are_left_alone()
    {
        var context = Run(new { plan = "free" });
        context.Result = new NotFoundResult();

        new ETagAttribute().OnActionExecuted(context);

        Assert.IsType<NotFoundResult>(context.Result);
    }
}
