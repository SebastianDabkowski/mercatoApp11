using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Api.Notifications;

[Authorize]
[IgnoreAntiforgeryToken]
public class PushModel : PageModel
{
    private readonly PushNotificationOptions _options;
    private readonly PushSubscriptionStore _subscriptions;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TimeProvider _timeProvider;

    public PushModel(
        PushNotificationOptions options,
        PushSubscriptionStore subscriptions,
        UserManager<ApplicationUser> userManager,
        TimeProvider timeProvider)
    {
        _options = options;
        _subscriptions = subscriptions;
        _userManager = userManager;
        _timeProvider = timeProvider;
    }

    public IActionResult OnGet()
    {
        return new JsonResult(new
        {
            enabled = _options.Enabled,
            publicKey = _options.PublicKey
        });
    }

    public async Task<IActionResult> OnPostAsync([FromBody] PushSubscribeRequest? request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        if (!_options.Enabled || request == null || string.IsNullOrWhiteSpace(request.Endpoint) || request.Keys == null)
        {
            return BadRequest(new { message = "Invalid subscription." });
        }

        _subscriptions.Save(
            user.Id,
            new PushSubscriptionEntry(
                request.Endpoint.Trim(),
                request.Keys.P256dh?.Trim() ?? string.Empty,
                request.Keys.Auth?.Trim() ?? string.Empty,
                _timeProvider.GetUtcNow()));

        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnDeleteAsync([FromBody] PushUnsubscribeRequest? request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        if (request != null)
        {
            _subscriptions.Remove(user.Id, request.Endpoint);
        }

        return new JsonResult(new { success = true });
    }
}

public record PushSubscribeRequest(string Endpoint, PushSubscriptionKeys Keys);

public record PushSubscriptionKeys(string P256dh, string Auth);

public record PushUnsubscribeRequest(string? Endpoint);
