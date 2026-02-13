using System.Net;
using System.Text.Json;
using WebPush;

namespace SD.ProjectName.WebApp.Services;

public interface IPushNotificationDispatcher
{
    Task DispatchAsync(string userId, NotificationItem item, CancellationToken cancellationToken);
}

public class PushNotificationDispatcher : IPushNotificationDispatcher
{
    private readonly PushNotificationOptions _options;
    private readonly PushSubscriptionStore _subscriptionStore;
    private readonly WebPushClient _client;
    private readonly ILogger<PushNotificationDispatcher> _logger;

    public PushNotificationDispatcher(
        PushNotificationOptions options,
        PushSubscriptionStore subscriptionStore,
        WebPushClient client,
        ILogger<PushNotificationDispatcher> logger)
    {
        _options = options;
        _subscriptionStore = subscriptionStore;
        _client = client;
        _logger = logger;
    }

    public async Task DispatchAsync(string userId, NotificationItem item, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.PublicKey) || string.IsNullOrWhiteSpace(_options.PrivateKey))
        {
            _logger.LogDebug("Push notifications skipped because VAPID keys are not configured.");
            return;
        }

        var subscriptions = _subscriptionStore.Get(userId);
        if (subscriptions.Count == 0)
        {
            return;
        }

        var vapidDetails = new VapidDetails(_options.Subject ?? "mailto:support@mercato.test", _options.PublicKey, _options.PrivateKey);
        var payload = JsonSerializer.Serialize(new
        {
            title = item.Title,
            body = item.Description,
            url = item.TargetUrl,
            category = item.Category,
            notificationId = item.Id
        });

        foreach (var subscription in subscriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);

            try
            {
                await _client.SendNotificationAsync(pushSubscription, payload, vapidDetails, cancellationToken);
            }
            catch (WebPushException ex) when (ex.StatusCode == HttpStatusCode.Gone || ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Removing expired push subscription for user {UserId}", userId);
                _subscriptionStore.Remove(userId, subscription.Endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send push notification to {Endpoint}", subscription.Endpoint);
            }
        }
    }
}
