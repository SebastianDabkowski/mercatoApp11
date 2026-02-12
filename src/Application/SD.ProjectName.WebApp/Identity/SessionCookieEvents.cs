using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace SD.ProjectName.WebApp.Identity
{
    public class SessionCookieEvents : CookieAuthenticationEvents
    {
        private readonly ISessionTokenService _sessionTokens;
        private readonly SessionTokenOptions _options;
        private readonly TimeProvider _timeProvider;

        public SessionCookieEvents(
            ISessionTokenService sessionTokens,
            SessionTokenOptions options,
            TimeProvider timeProvider)
        {
            _sessionTokens = sessionTokens;
            _options = options;
            _timeProvider = timeProvider;
        }

        public override async Task SigningIn(CookieSigningInContext context)
        {
            var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return;
            }

            var sessionToken = await _sessionTokens.IssueAsync(userId, context.HttpContext.RequestAborted);
            context.Response.Cookies.Append(_options.CookieName, sessionToken.Token, BuildCookieOptions(context.Options, sessionToken.ExpiresAt));
        }

        public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
        {
            var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var token = context.HttpContext.Request.Cookies[_options.CookieName];

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            {
                await InvalidateAsync(context);
                return;
            }

            var valid = await _sessionTokens.ValidateAsync(userId, token, context.HttpContext.RequestAborted);
            if (!valid)
            {
                await InvalidateAsync(context);
            }
        }

        public override async Task SigningOut(CookieSigningOutContext context)
        {
            var userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var token = context.HttpContext.Request.Cookies[_options.CookieName];

            if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(token))
            {
                await _sessionTokens.RevokeAsync(userId, token, context.HttpContext.RequestAborted);
            }

            context.Response.Cookies.Delete(_options.CookieName, BuildCookieOptions(context.Options, _timeProvider.GetUtcNow()));
        }

        private static CookieOptions BuildCookieOptions(CookieAuthenticationOptions options, DateTimeOffset expiresAt)
        {
            return new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Expires = expiresAt.UtcDateTime,
                Path = options.Cookie.Path
            };
        }

        private async Task InvalidateAsync(CookieValidatePrincipalContext context)
        {
            context.RejectPrincipal();
            var cookieOptions = BuildCookieOptions(context.Options, _timeProvider.GetUtcNow());
            if (!string.IsNullOrEmpty(context.Options.Cookie.Name))
            {
                context.HttpContext.Response.Cookies.Delete(context.Options.Cookie.Name!, cookieOptions);
            }
            context.HttpContext.Response.Cookies.Delete(_options.CookieName, cookieOptions);
            await context.HttpContext.SignOutAsync();
        }
    }
}
