using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Api
{
    [IgnoreAntiforgeryToken]
    public class CartModel : PageModel
    {
        private readonly GetProducts _getProducts;
        private readonly CartService _cartService;
        private readonly CartViewService _cartViewService;
        private readonly IUserCartService _userCartService;
        private readonly IAnalyticsTracker? _analyticsTracker;

        public CartModel(GetProducts getProducts, CartService cartService, CartViewService cartViewService, IUserCartService userCartService, IAnalyticsTracker? analyticsTracker = null)
        {
            _getProducts = getProducts;
            _cartService = cartService;
            _cartViewService = cartViewService;
            _userCartService = userCartService;
            _analyticsTracker = analyticsTracker;
        }

        public async Task<IActionResult> OnPostAddAsync([FromBody] AddToCartRequest request)
        {
            if (request == null || request.ProductId <= 0)
            {
                return BadRequest(new { message = "Invalid product." });
            }

            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);

            var product = await _getProducts.GetById(request.ProductId);
            if (product == null)
            {
                return NotFound(new { message = "This product is unavailable." });
            }

            var attributes = NormalizeAttributes(request.VariantAttributes);

            if (product.HasVariants)
            {
                if (attributes.Count == 0)
                {
                    return BadRequest(new { message = "Please select a variant." });
                }

                var variant = FindVariant(product, attributes);
                if (variant == null)
                {
                    return BadRequest(new { message = "Selected variant is unavailable." });
                }

                if (variant.Stock <= 0)
                {
                    return BadRequest(new { message = "Selected variant is out of stock." });
                }
            }
            else if (product.Stock <= 0)
            {
                return BadRequest(new { message = "This item is out of stock." });
            }

            var stockCheck = ResolveAvailability(product, attributes);
            if (stockCheck.VariantMissing)
            {
                return BadRequest(new { message = "Selected variant is unavailable." });
            }

            if (stockCheck.AvailableStock <= 0)
            {
                return BadRequest(new { message = "This item is out of stock." });
            }

            var existingItems = _cartService.GetItems(HttpContext);
            var existingQuantity = existingItems.FirstOrDefault(i => i.ProductId == product.Id && AreSameAttributes(i.VariantAttributes, attributes))?.Quantity ?? 0;
            var availableForAdd = Math.Max(stockCheck.AvailableStock - existingQuantity, 0);
            if (availableForAdd <= 0)
            {
                return BadRequest(new { message = "You already have the maximum available stock in your cart." });
            }

            var requestedQuantity = request.Quantity <= 0 ? 1 : request.Quantity;
            var allowedQuantity = Math.Min(requestedQuantity, availableForAdd);
            var result = _cartService.AddProduct(HttpContext, product, attributes, allowedQuantity);
            var message = allowedQuantity < requestedQuantity
                ? $"Only {availableForAdd} more in stock. Quantity adjusted."
                : result.Status == CartUpdateStatus.Added
                    ? "Added to cart."
                    : "Quantity updated.";

            await _userCartService.PersistAuthenticatedCartAsync(HttpContext, HttpContext.RequestAborted);
            if (_analyticsTracker != null)
            {
                await _analyticsTracker.TrackAsync(
                    new AnalyticsEventEntry(
                        AnalyticsEventTypes.AddToCart,
                        ProductId: product.Id,
                        SellerId: product.SellerId,
                        Quantity: allowedQuantity,
                        Metadata: attributes.ToDictionary(k => k.Key, v => (string?)v.Value, StringComparer.OrdinalIgnoreCase)),
                    HttpContext.RequestAborted);
            }

            return new JsonResult(new AddToCartResponse(true, message, result.Quantity, result.Status.ToString().ToLowerInvariant()));
        }

        public async Task<IActionResult> OnPostUpdateQuantityAsync([FromBody] UpdateQuantityRequest request)
        {
            if (request == null || request.ProductId <= 0)
            {
                return BadRequest(new { message = "Invalid request." });
            }

            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);

            var normalizedAttributes = NormalizeAttributes(request.VariantAttributes);
            var items = _cartService.GetItems(HttpContext);
            var existing = items.FirstOrDefault(i => i.ProductId == request.ProductId && AreSameAttributes(i.VariantAttributes, normalizedAttributes));
            if (existing == null)
            {
                var missingSummary = await _cartViewService.BuildAsync(HttpContext, items);
                return NotFound(new CartUpdateResponse(false, "Item not found in cart.", false, 0, 0, 0, ToTotals(missingSummary)));
            }

            if (request.Quantity <= 0)
            {
                items.Remove(existing);
                _cartService.ReplaceCart(HttpContext, items);
                var removedSummary = await _cartViewService.BuildAsync(HttpContext, items);
                await _userCartService.PersistAuthenticatedCartAsync(HttpContext, HttpContext.RequestAborted);
                return new JsonResult(new CartUpdateResponse(true, "Item removed.", true, 0, 0, 0, ToTotals(removedSummary)));
            }

            var product = await _getProducts.GetById(request.ProductId);
            if (product == null)
            {
                items.Remove(existing);
                _cartService.ReplaceCart(HttpContext, items);
                var missingSummary = await _cartViewService.BuildAsync(HttpContext, items);
                await _userCartService.PersistAuthenticatedCartAsync(HttpContext, HttpContext.RequestAborted);
                return NotFound(new CartUpdateResponse(false, "This product is unavailable.", true, 0, 0, 0, ToTotals(missingSummary)));
            }

            var stockCheck = ResolveAvailability(product, normalizedAttributes);
            if (stockCheck.VariantMissing)
            {
                items.Remove(existing);
                _cartService.ReplaceCart(HttpContext, items);
                var missingSummary = await _cartViewService.BuildAsync(HttpContext, items);
                await _userCartService.PersistAuthenticatedCartAsync(HttpContext, HttpContext.RequestAborted);
                return new JsonResult(new CartUpdateResponse(true, "Selected variant is unavailable and was removed.", true, 0, 0, 0, ToTotals(missingSummary)));
            }

            if (stockCheck.AvailableStock <= 0)
            {
                items.Remove(existing);
                _cartService.ReplaceCart(HttpContext, items);
                var removedSummary = await _cartViewService.BuildAsync(HttpContext, items);
                await _userCartService.PersistAuthenticatedCartAsync(HttpContext, HttpContext.RequestAborted);
                return new JsonResult(new CartUpdateResponse(true, "Item is out of stock and was removed.", true, 0, 0, 0, ToTotals(removedSummary)));
            }

            var clampedQuantity = Math.Min(Math.Max(request.Quantity, 1), stockCheck.AvailableStock);
            existing.Quantity = clampedQuantity;
            _cartService.ReplaceCart(HttpContext, items);

            var summary = await _cartViewService.BuildAsync(HttpContext, items);
            var message = clampedQuantity < request.Quantity
                ? $"Quantity adjusted to available stock ({stockCheck.AvailableStock})."
                : "Quantity updated.";
            var lineTotal = stockCheck.UnitPrice * clampedQuantity;

            await _userCartService.PersistAuthenticatedCartAsync(HttpContext, HttpContext.RequestAborted);
            return new JsonResult(new CartUpdateResponse(true, message, false, clampedQuantity, lineTotal, stockCheck.AvailableStock, ToTotals(summary)));
        }

        public async Task<IActionResult> OnPostRemoveAsync([FromBody] RemoveFromCartRequest request)
        {
            if (request == null || request.ProductId <= 0)
            {
                return BadRequest(new { message = "Invalid request." });
            }

            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);

            var normalizedAttributes = NormalizeAttributes(request.VariantAttributes);
            var items = _cartService.GetItems(HttpContext);
            var removed = items.RemoveAll(i => i.ProductId == request.ProductId && AreSameAttributes(i.VariantAttributes, normalizedAttributes)) > 0;
            if (removed)
            {
                _cartService.ReplaceCart(HttpContext, items);
            }

            var summary = await _cartViewService.BuildAsync(HttpContext, items);
            if (!removed)
            {
                return NotFound(new CartUpdateResponse(false, "Item not found in cart.", false, 0, 0, 0, ToTotals(summary)));
            }

            await _userCartService.PersistAuthenticatedCartAsync(HttpContext, HttpContext.RequestAborted);
            return new JsonResult(new CartUpdateResponse(true, "Item removed.", true, 0, 0, 0, ToTotals(summary)));
        }

        private static (int AvailableStock, decimal UnitPrice, bool VariantMissing) ResolveAvailability(ProductModel product, IReadOnlyDictionary<string, string> attributes)
        {
            if (product.HasVariants)
            {
                if (attributes.Count == 0)
                {
                    return (0, 0, true);
                }

                var variant = FindVariant(product, attributes);
                if (variant == null)
                {
                    return (0, 0, true);
                }

                return (Math.Max(0, variant.Stock), variant.Price, false);
            }

            return (Math.Max(0, product.Stock), product.Price, false);
        }

        private static ProductVariant? FindVariant(ProductModel product, IReadOnlyDictionary<string, string> attributes)
        {
            if (product.Variants == null || product.Variants.Count == 0)
            {
                return null;
            }

            return product.Variants.FirstOrDefault(v => AttributesMatch(v.Attributes, attributes));
        }

        private static bool AttributesMatch(IDictionary<string, string> variantAttributes, IReadOnlyDictionary<string, string> requested)
        {
            if (variantAttributes.Count != requested.Count)
            {
                return false;
            }

            foreach (var pair in variantAttributes)
            {
                if (!requested.TryGetValue(pair.Key, out var value) || !string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, string> NormalizeAttributes(IReadOnlyDictionary<string, string>? attributes)
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

        private static bool AreSameAttributes(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
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

        private static CartTotalsDto ToTotals(CartSummary summary)
        {
            return new CartTotalsDto(
                summary.ItemsSubtotal,
                summary.ShippingTotal,
                summary.GrandTotal,
                summary.TotalQuantity,
                summary.SellerGroups.Select(g => new CartSellerTotalsDto(g.SellerId, g.Subtotal, g.Shipping, g.Total)).ToList(),
                summary.DiscountTotal,
                summary.AppliedPromoCode);
        }
    }

    public record AddToCartRequest
    {
        public int ProductId { get; init; }

        public int Quantity { get; init; } = 1;

        public Dictionary<string, string>? VariantAttributes { get; init; }
    }

    public record AddToCartResponse(bool Success, string Message, int Quantity, string Status);

    public record UpdateQuantityRequest
    {
        public int ProductId { get; init; }

        public int Quantity { get; init; }

        public Dictionary<string, string>? VariantAttributes { get; init; }
    }

    public record RemoveFromCartRequest
    {
        public int ProductId { get; init; }

        public Dictionary<string, string>? VariantAttributes { get; init; }
    }

    public record CartTotalsDto(decimal ItemsSubtotal, decimal ShippingTotal, decimal GrandTotal, int TotalQuantity, List<CartSellerTotalsDto> Sellers, decimal DiscountTotal, string? PromoCode);

    public record CartSellerTotalsDto(string SellerId, decimal Subtotal, decimal Shipping, decimal Total);

    public record CartUpdateResponse(bool Success, string Message, bool Removed, int Quantity, decimal LineTotal, int AvailableStock, CartTotalsDto Totals);
}
