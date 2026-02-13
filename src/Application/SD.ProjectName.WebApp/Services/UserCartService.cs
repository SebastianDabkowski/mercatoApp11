using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public interface IUserCartService
    {
        Task EnsureUserCartAsync(HttpContext context, CancellationToken cancellationToken = default);

        Task MergeOnSignInAsync(HttpContext context, ApplicationUser user, CancellationToken cancellationToken = default);

        Task PersistAuthenticatedCartAsync(HttpContext context, CancellationToken cancellationToken = default);
    }

    public class UserCartService : IUserCartService
    {
        private readonly CartService _cartService;
        private readonly CartViewService _cartViewService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<UserCartService> _logger;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public UserCartService(
            CartService cartService,
            CartViewService cartViewService,
            UserManager<ApplicationUser> userManager,
            ILogger<UserCartService> logger)
        {
            _cartService = cartService;
            _cartViewService = cartViewService;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task EnsureUserCartAsync(HttpContext context, CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var items = _cartService.GetItems(context);
            if (items.Count > 0 || !(context.User?.Identity?.IsAuthenticated ?? false))
            {
                return;
            }

            var user = await _userManager.GetUserAsync(context.User);
            if (user == null)
            {
                return;
            }

            var storedItems = Deserialize(user);
            if (storedItems.Count == 0)
            {
                return;
            }

            _cartService.ReplaceCart(context, storedItems);
            await _cartViewService.BuildAsync(context, storedItems);
            SyncRequestCookie(context);
            await SaveAsync(context, user, storedItems, cancellationToken);
        }

        public async Task MergeOnSignInAsync(HttpContext context, ApplicationUser user, CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var guestItems = _cartService.GetItems(context);
            var storedItems = Deserialize(user);
            if (guestItems.Count == 0 && storedItems.Count == 0)
            {
                return;
            }

            var merged = MergeItems(storedItems, guestItems);
            _cartService.ReplaceCart(context, merged);
            await _cartViewService.BuildAsync(context, merged);
            SyncRequestCookie(context);
            await SaveAsync(context, user, merged, cancellationToken);
        }

        public async Task PersistAuthenticatedCartAsync(HttpContext context, CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (!(context.User?.Identity?.IsAuthenticated ?? false))
            {
                return;
            }

            var user = await _userManager.GetUserAsync(context.User);
            if (user == null)
            {
                return;
            }

            SyncRequestCookie(context);
            await SaveAsync(context, user, _cartService.GetItems(context), cancellationToken);
        }

        private async Task SaveAsync(HttpContext context, ApplicationUser user, IReadOnlyCollection<CartItem> items, CancellationToken cancellationToken)
        {
            var normalized = ResolveItemsForSave(context, items);
            user.CartData = normalized.Count == 0
                ? null
                : JsonSerializer.Serialize(new CartPayload(normalized), _serializerOptions);

            await _userManager.UpdateAsync(user);
        }

        private List<CartItem> ResolveItemsForSave(HttpContext context, IReadOnlyCollection<CartItem> items)
        {
            var cookieValue = GetCookieValueFromResponse(context);
            if (!string.IsNullOrWhiteSpace(cookieValue))
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<CartPayload>(cookieValue, _serializerOptions);
                    if (payload?.Items != null)
                    {
                        return _cartService.NormalizeItems(payload.Items);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read cart cookie from response.");
                }
            }

            return _cartService.NormalizeItems(items);
        }

        private string? GetCookieValueFromResponse(HttpContext context)
        {
            var header = context.Response.Headers["Set-Cookie"].ToString();
            var prefix = $"{_cartService.CookieName}=";
            var start = header.LastIndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            var end = header.IndexOf(';', start);
            var encoded = end > start ? header[(start + prefix.Length)..end] : header[(start + prefix.Length)..];
            if (string.IsNullOrWhiteSpace(encoded))
            {
                return null;
            }

            return Uri.UnescapeDataString(encoded);
        }

        private void SyncRequestCookie(HttpContext context)
        {
            var value = GetCookieValueFromResponse(context);
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { _cartService.CookieName, value }
            };

            context.Features.Set<IRequestCookiesFeature>(new RequestCookiesFeature(new DictionaryCookieCollection(cookies)));
        }

        private sealed class DictionaryCookieCollection : IRequestCookieCollection
        {
            private readonly Dictionary<string, string> _cookies;

            public DictionaryCookieCollection(Dictionary<string, string> cookies)
            {
                _cookies = cookies;
            }

            public string this[string key] => _cookies.TryGetValue(key, out var value) ? value : string.Empty;

            public int Count => _cookies.Count;

            public ICollection<string> Keys => _cookies.Keys;

            public bool ContainsKey(string key) => _cookies.ContainsKey(key);

            public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _cookies.GetEnumerator();

            public bool TryGetValue(string key, out string value)
            {
                var found = _cookies.TryGetValue(key, out var stored);
                value = stored ?? string.Empty;
                return found;
            }

            IEnumerator IEnumerable.GetEnumerator() => _cookies.GetEnumerator();
        }

        private List<CartItem> Deserialize(ApplicationUser user)
        {
            if (string.IsNullOrWhiteSpace(user.CartData))
            {
                return new List<CartItem>();
            }

            try
            {
                var payload = JsonSerializer.Deserialize<CartPayload>(user.CartData, _serializerOptions);
                if (payload?.Items == null)
                {
                    return new List<CartItem>();
                }

                return _cartService.NormalizeItems(payload.Items);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read stored cart for user {UserId}.", user.Id);
                return new List<CartItem>();
            }
        }

        private static List<CartItem> MergeItems(IEnumerable<CartItem> existingItems, IEnumerable<CartItem> incomingItems)
        {
            var merged = new List<CartItem>();
            foreach (var item in existingItems.Concat(incomingItems))
            {
                if (item.ProductId <= 0 || string.IsNullOrWhiteSpace(item.SellerId))
                {
                    continue;
                }

                var normalizedAttributes = CartService.NormalizeAttributes(item.VariantAttributes);
                var existing = merged.FirstOrDefault(i => i.ProductId == item.ProductId && CartService.AreSameAttributes(i.VariantAttributes, normalizedAttributes));
                if (existing != null)
                {
                    existing.Quantity += item.Quantity <= 0 ? 1 : item.Quantity;
                    continue;
                }

                merged.Add(new CartItem
                {
                    ProductId = item.ProductId,
                    SellerId = item.SellerId,
                    Quantity = item.Quantity <= 0 ? 1 : item.Quantity,
                    VariantAttributes = normalizedAttributes
                });
            }

            return merged;
        }
    }
}
