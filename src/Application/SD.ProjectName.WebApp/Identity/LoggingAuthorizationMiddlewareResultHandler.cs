using System.Security.Claims;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SD.ProjectName.WebApp.Identity
{
    public class LoggingAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();
        private readonly ILogger<LoggingAuthorizationMiddlewareResultHandler> _logger;

        public LoggingAuthorizationMiddlewareResultHandler(ILogger<LoggingAuthorizationMiddlewareResultHandler> logger)
        {
            _logger = logger;
        }

        public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (!authorizeResult.Succeeded)
            {
                var userName = context.User.Identity?.IsAuthenticated == true
                    ? context.User.Identity?.Name ?? "AuthenticatedUser"
                    : "Anonymous";
                var roles = context.User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray();
                _logger.LogWarning("Authorization failure for {User} on {Path}. Roles: {Roles}", userName, context.Request.Path, roles.Length == 0 ? "none" : string.Join(",", roles));
            }

            await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
        }
    }
}
