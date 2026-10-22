using DATA_EXCHANGE_HUB.Pages.Hub;
using Microsoft.Extensions.Localization;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class HubModuleAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IHubModuleAccess access, IStringLocalizer<SharedResource> localizer)
    {
        var path = context.Request.Path.Value ?? "/";
        if (HubModuleAccess.ApplicationName(path) is not { } applicationName)
        {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        var blocked = context.Session.GetString(LoginModel.BlockedSessionKey) == "1";
        if (!blocked && await access.CanOpenAsync(path, context.User, context.RequestAborted))
        {
            await next(context);
            return;
        }

        if (HttpMethods.IsGet(context.Request.Method) && !context.Request.Query.ContainsKey("handler"))
            context.Response.Redirect($"/Hub/AccessDenied?Application={Uri.EscapeDataString(applicationName)}");
        else
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync(localizer["Hub.Security.NoAccess"].Value, context.RequestAborted);
        }
    }
}
