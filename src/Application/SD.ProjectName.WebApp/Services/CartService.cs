using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Domain;

namespace SD.ProjectName.WebApp.Services
{
    public class CartService
    {
        private const int MaxAllowedItems = 100;
        private readonly CartOptions _options;
        private readonly ILogger<CartService> _logger;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        internal string CookieName => _options.CookieName;

        public CartService(CartOptions options, ILogger<CartService> logger)
        {
            _options = options;
            _logger = logger;
        }

        public List<CartItem> GetItems(HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return ReadCart(context);
        }

        public CartUpdateResult AddProduct(HttpContext context, ProductModel product, IReadOnlyDictionary<string, string>? variantAttributes = null, int quantity = 1)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (product == null)
            {
                throw new ArgumentNullException(nameof(product));
            }

            var normalizedQuantity = quantity <= 0 ? 1 : quantity;
            var items = ReadCart(context);
            var normalizedAttributes = NormalizeAttributes(variantAttributes);
            var existing = items.FirstOrDefault(i => i.ProductId == product.Id && AreSameAttributes(i.VariantAttributes, normalizedAttributes));
            if (existing != null)
            {
                existing.Quantity += normalizedQuantity;
                WriteCart(context, items);
                return CartUpdateResult.Incremented(existing.Quantity);
            }

            var limit = GetLimit();
            var item = new CartItem
            {
                ProductId = product.Id,
                SellerId = product.SellerId,
                Quantity = normalizedQuantity,
                VariantAttributes = normalizedAttributes
            };

            items.Insert(0, item);
            if (items.Count > limit)
            {
                items = items.Take(limit).ToList();
            }

            WriteCart(context, items);
            return CartUpdateResult.Added(normalizedQuantity);
        }

        public void ReplaceCart(HttpContext context, IReadOnlyCollection<CartItem> items)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var normalized = NormalizeItems(items);
            WriteCart(context, normalized);
        }

        private List<CartItem> ReadCart(HttpContext context)
        {
            if (!context.Request.Cookies.TryGetValue(_options.CookieName, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return new List<CartItem>();
            }

            try
            {
                var payload = JsonSerializer.Deserialize<CartPayload>(value, _serializerOptions);
                if (payload?.Items == null || payload.Items.Count == 0)
                {
                    return new List<CartItem>();
                }

                return NormalizeItems(payload.Items);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read cart cookie, resetting it.");
                return new List<CartItem>();
            }
        }

        internal List<CartItem> NormalizeItems(IEnumerable<CartItem> items)
        {
            var limit = GetLimit();
            var result = new List<CartItem>();
            foreach (var item in items)
            {
                if (item.ProductId <= 0 || string.IsNullOrWhiteSpace(item.SellerId))
                {
                    continue;
                }

                var normalizedAttributes = NormalizeAttributes(item.VariantAttributes);
                var existing = result.FirstOrDefault(i => i.ProductId == item.ProductId && AreSameAttributes(i.VariantAttributes, normalizedAttributes));
                if (existing != null)
                {
                    existing.Quantity += item.Quantity <= 0 ? 1 : item.Quantity;
                    continue;
                }

                result.Add(new CartItem
                {
                    ProductId = item.ProductId,
                    SellerId = item.SellerId,
                    Quantity = item.Quantity <= 0 ? 1 : item.Quantity,
                    VariantAttributes = normalizedAttributes
                });

                if (result.Count >= limit)
                {
                    break;
                }
            }

            return result;
        }

        private void WriteCart(HttpContext context, IEnumerable<CartItem> items)
        {
            var normalized = NormalizeItems(items);
            if (normalized.Count == 0)
            {
                context.Response.Cookies.Delete(_options.CookieName, BuildCookieOptions());
                return;
            }

            var payload = new CartPayload(normalized);
            var value = JsonSerializer.Serialize(payload, _serializerOptions);
            context.Response.Cookies.Append(_options.CookieName, value, BuildCookieOptions());
        }

        private CookieOptions BuildCookieOptions()
        {
            return new CookieOptions
            {
                HttpOnly = true,
                IsEssential = false,
                SameSite = SameSiteMode.Lax,
                Secure = true,
                Expires = DateTimeOffset.UtcNow.AddDays(_options.CookieLifespanDays),
                Path = "/"
            };
        }

        private int GetLimit()
        {
            var limit = _options.MaxItems;
            if (limit <= 0)
            {
                _logger.LogWarning("Cart MaxItems misconfigured as {MaxItems}. Falling back to 1.", limit);
                return 1;
            }

            return Math.Min(limit, MaxAllowedItems);
        }

        internal static Dictionary<string, string> NormalizeAttributes(IReadOnlyDictionary<string, string>? attributes)
        {
            if (attributes == null || attributes.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in attributes)
            {
                var key = pair.Key?.Trim();
                var value = pair.Value?.Trim();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                normalized[key] = value;
            }

            return normalized;
        }

        internal static bool AreSameAttributes(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var value) || !string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public record CartPayload(List<CartItem> Items);

    public class CartItem
    {
        public int ProductId { get; set; }

        public string SellerId { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public IReadOnlyDictionary<string, string> VariantAttributes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public record CartUpdateResult(CartUpdateStatus Status, int Quantity)
    {
        public static CartUpdateResult Added(int quantity) => new(CartUpdateStatus.Added, quantity);

        public static CartUpdateResult Incremented(int quantity) => new(CartUpdateStatus.Incremented, quantity);
    }

    public enum CartUpdateStatus
    {
        Added,
        Incremented
    }
}
