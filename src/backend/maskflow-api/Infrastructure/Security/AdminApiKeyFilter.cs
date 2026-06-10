using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MaskFlow.Api.Infrastructure.Security;

public sealed class AdminApiKeyFilter : IAsyncAuthorizationFilter
{
    static bool FixedTimeEquals(string provided, string expected)
    {
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var path = context.HttpContext.Request.Path;
        if (!path.StartsWithSegments("/v1"))
        {
            return Task.CompletedTask;
        }

        if (path.StartsWithSegments("/v1/wallet/balance"))
        {
            return Task.CompletedTask;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<MaskFlowStore>();
        var expectedAdmin = Environment.GetEnvironmentVariable("MASKFLOW_ADMIN_API_KEY");
        var adminKey = context.HttpContext.Request.Headers["X-Admin-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(expectedAdmin) && FixedTimeEquals(adminKey, expectedAdmin))
        {
            context.HttpContext.Items[MaskFlowHttpItems.AdminAccess] = true;
            return Task.CompletedTask;
        }

        if (IsUserScopedJobEndpoint(path, context.HttpContext.Request.Method)
            && store.OptionalUser(context.HttpContext) is User user)
        {
            context.HttpContext.Items[MaskFlowHttpItems.AuthenticatedUser] = user;
            return Task.CompletedTask;
        }

        if (TryGetNodeId(path, out var nodeId))
        {
            var nodeKey = context.HttpContext.Request.Headers["X-Node-Key"].ToString();
            if (store.ValidateNodeKey(nodeId, nodeKey))
            {
                return Task.CompletedTask;
            }
        }

        if (string.IsNullOrWhiteSpace(expectedAdmin))
        {
            context.Result = new ObjectResult(new { detail = "ComputeHub is disabled. Set MASKFLOW_ADMIN_API_KEY." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return Task.CompletedTask;
        }

        context.Result = new UnauthorizedObjectResult(new { detail = "Admin or node key required." });
        return Task.CompletedTask;
    }

    static bool TryGetNodeId(PathString path, out string nodeId)
    {
        nodeId = "";
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length < 3 || !segments[0].Equals("v1", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("nodes", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var action = segments.Length > 3 ? segments[3] : "";
        if (!action.Equals("heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        nodeId = segments[2];
        return !string.IsNullOrWhiteSpace(nodeId);
    }

    static bool IsUserScopedJobEndpoint(PathString path, string method)
    {
        if (!path.StartsWithSegments("/v1/jobs"))
        {
            return false;
        }

        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length == 2)
        {
            return method is "GET" or "POST";
        }

        if (segments.Length == 3)
        {
            return method == "GET";
        }

        return false;
    }
}
