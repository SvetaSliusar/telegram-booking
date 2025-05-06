using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Telegram.Bot.Filters;

[AttributeUsage(AttributeTargets.Method)]
public class RequestTimeoutAttribute : ActionFilterAttribute
{
    private readonly int _timeoutInSeconds;

    public RequestTimeoutAttribute(int timeoutInSeconds)
    {
        _timeoutInSeconds = timeoutInSeconds;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutInSeconds));
        var originalToken = context.HttpContext.RequestAborted;
        context.HttpContext.RequestAborted = cts.Token;

        try
        {
            await next();
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            context.Result = new StatusCodeResult(504);
        }
        finally
        {
            context.HttpContext.RequestAborted = originalToken;
        }
    }
} 