using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public static class AnalyticsEventTypes
    {
        public const string Search = "search";
        public const string ProductView = "product_view";
        public const string AddToCart = "add_to_cart";
        public const string CheckoutStart = "checkout_start";
        public const string OrderCompleted = "order_completed";
    }

    public record AnalyticsEventEntry(
        string EventType,
        string? UserId = null,
        string? SessionId = null,
        int? ProductId = null,
        string? SellerId = null,
        int? OrderId = null,
        string? Keyword = null,
        int? Quantity = null,
        decimal? Amount = null,
        IReadOnlyDictionary<string, string?>? Metadata = null);

    public interface IAnalyticsTracker
    {
        Task TrackAsync(AnalyticsEventEntry entry, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AnalyticsEvent>> QueryAsync(DateTimeOffset from, DateTimeOffset to, string? eventType = null, CancellationToken cancellationToken = default);
    }

    public class AnalyticsTracker : IAnalyticsTracker
    {
        private const string RequestCountKey = "__analytics_event_count";
        private readonly ApplicationDbContext _dbContext;
        private readonly AnalyticsOptions _options;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<AnalyticsTracker> _logger;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public AnalyticsTracker(
            ApplicationDbContext dbContext,
            AnalyticsOptions options,
            IHttpContextAccessor httpContextAccessor,
            TimeProvider timeProvider,
            ILogger<AnalyticsTracker> logger)
        {
            _dbContext = dbContext;
            _options = options;
            _httpContextAccessor = httpContextAccessor;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task TrackAsync(AnalyticsEventEntry entry, CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || entry == null || string.IsNullOrWhiteSpace(entry.EventType))
            {
                return;
            }

            var context = _httpContextAccessor.HttpContext;
            if (IsOverPerRequestLimit(context))
            {
                return;
            }

            var sessionId = entry.SessionId ?? ResolveSessionId(context);
            var userId = entry.UserId ?? ResolveUserId(context);
            if (string.IsNullOrWhiteSpace(sessionId) && string.IsNullOrWhiteSpace(userId))
            {
                sessionId = context?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
            }

            var analyticsEvent = new AnalyticsEvent
            {
                EventType = entry.EventType.Trim(),
                OccurredOn = _timeProvider.GetUtcNow(),
                UserId = userId,
                SessionId = sessionId,
                ProductId = entry.ProductId,
                SellerId = entry.SellerId,
                OrderId = entry.OrderId,
                Keyword = string.IsNullOrWhiteSpace(entry.Keyword) ? null : entry.Keyword.Trim(),
                Quantity = entry.Quantity,
                Amount = entry.Amount,
                MetadataJson = SerializeMetadata(entry.Metadata)
            };

            try
            {
                await _dbContext.AnalyticsEvents.AddAsync(analyticsEvent, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                IncrementRequestCount(context);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to record analytics event {EventType}", entry.EventType);
            }
        }

        public async Task<IReadOnlyList<AnalyticsEvent>> QueryAsync(DateTimeOffset from, DateTimeOffset to, string? eventType = null, CancellationToken cancellationToken = default)
        {
            var query = _dbContext.AnalyticsEvents.AsNoTracking()
                .Where(e => e.OccurredOn >= from && e.OccurredOn <= to);

            if (!string.IsNullOrWhiteSpace(eventType))
            {
                query = query.Where(e => e.EventType == eventType);
            }

            return await query
                .OrderBy(e => e.OccurredOn)
                .ToListAsync(cancellationToken);
        }

        private string? ResolveSessionId(HttpContext? context)
        {
            if (context == null)
            {
                return null;
            }

            if (context.Request.Cookies.TryGetValue(_options.SessionCookieName, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            var sessionId = Guid.NewGuid().ToString("N");
            context.Response.Cookies.Append(_options.SessionCookieName, sessionId, new CookieOptions
            {
                HttpOnly = false,
                IsEssential = false,
                SameSite = SameSiteMode.Lax,
                Secure = true,
                Expires = _timeProvider.GetUtcNow().AddDays(_options.SessionLifespanDays)
            });

            return sessionId;
        }

        private static string? ResolveUserId(HttpContext? context)
        {
            if (context?.User?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        private string? SerializeMetadata(IReadOnlyDictionary<string, string?>? metadata)
        {
            if (metadata == null || metadata.Count == 0)
            {
                return null;
            }

            var filtered = metadata
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            return filtered.Count == 0 ? null : JsonSerializer.Serialize(filtered, _serializerOptions);
        }

        private bool IsOverPerRequestLimit(HttpContext? context)
        {
            if (context == null || _options.MaxEventsPerRequest <= 0)
            {
                return false;
            }

            if (!context.Items.TryGetValue(RequestCountKey, out var value) || value is not int count)
            {
                return false;
            }

            return count >= _options.MaxEventsPerRequest;
        }

        private void IncrementRequestCount(HttpContext? context)
        {
            if (context == null || _options.MaxEventsPerRequest <= 0)
            {
                return;
            }

            if (context.Items.TryGetValue(RequestCountKey, out var value) && value is int count)
            {
                context.Items[RequestCountKey] = count + 1;
                return;
            }

            context.Items[RequestCountKey] = 1;
        }
    }
}
