using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public class CartViewService
    {
        private readonly CartService _cartService;
        private readonly CartTotalsCalculator _totalsCalculator;
        private readonly GetProducts _getProducts;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly PromoCodeService _promoCodeService;

        public CartViewService(
            CartService cartService,
            CartTotalsCalculator totalsCalculator,
            GetProducts getProducts,
            UserManager<ApplicationUser> userManager,
            PromoCodeService promoCodeService)
        {
            _cartService = cartService;
            _totalsCalculator = totalsCalculator;
            _getProducts = getProducts;
            _userManager = userManager;
            _promoCodeService = promoCodeService;
        }

        public async Task<CartSummary> BuildAsync(HttpContext httpContext)
        {
            var storedItems = _cartService.GetItems(httpContext);
            if (storedItems.Count == 0)
            {
                return CartSummary.Empty;
            }

            return await BuildAsync(httpContext, storedItems);
        }

        public async Task<CartSummary> BuildAsync(HttpContext httpContext, IReadOnlyCollection<CartItem> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var storedItems = items.ToList();
            if (storedItems.Count == 0)
            {
                return CartSummary.Empty;
            }

            var productIds = storedItems.Select(i => i.ProductId).Distinct().ToList();
            var products = await _getProducts.GetByIds(productIds, includeDrafts: false);
            var productLookup = products.ToDictionary(p => p.Id);
            var validItems = new List<CartItem>();
            var displayItems = new List<CartDisplayItem>();
            var needsReplace = false;

            foreach (var item in storedItems)
            {
                if (!productLookup.TryGetValue(item.ProductId, out var product))
                {
                    needsReplace = true;
                    continue;
                }

                var normalizedAttributes = NormalizeAttributes(item.VariantAttributes);
                var variantLabel = string.Empty;
                decimal unitPrice;
                int availableStock;

                if (product.HasVariants)
                {
                    var variant = FindVariant(product, normalizedAttributes);
                    if (variant == null)
                    {
                        needsReplace = true;
                        continue;
                    }

                    variantLabel = BuildVariantLabel(normalizedAttributes);
                    unitPrice = variant.Price;
                    availableStock = Math.Max(0, variant.Stock);
                }
                else
                {
                    unitPrice = product.Price;
                    availableStock = Math.Max(0, product.Stock);
                }

                if (availableStock <= 0)
                {
                    needsReplace = true;
                    continue;
                }

                var quantity = item.Quantity <= 0 ? 1 : item.Quantity;
                if (quantity > availableStock)
                {
                    quantity = availableStock;
                    needsReplace = true;
                }

                var normalizedItem = new CartItem
                {
                    ProductId = product.Id,
                    SellerId = product.SellerId,
                    Quantity = quantity,
                    VariantAttributes = normalizedAttributes
                };

                validItems.Add(normalizedItem);
                var lineTotal = unitPrice * quantity;
                displayItems.Add(new CartDisplayItem(product, quantity, variantLabel, unitPrice, lineTotal, true, availableStock, normalizedAttributes));
            }

            if (needsReplace || validItems.Count != storedItems.Count)
            {
                _cartService.ReplaceCart(httpContext, validItems);
            }

            if (displayItems.Count == 0)
            {
                return CartSummary.Empty;
            }

            var sellerProfiles = await LoadSellerProfilesAsync(displayItems.Select(d => d.Product.SellerId).Distinct());
            var sellerGroups = displayItems
                .GroupBy(d => d.Product.SellerId)
                .Select(group =>
                {
                    var sellerName = sellerProfiles.TryGetValue(group.Key, out var profile) ? profile.Name : "Seller";
                    var sellerType = sellerProfiles.TryGetValue(group.Key, out profile) ? profile.SellerType : string.Empty;
                    var subtotal = group.Sum(i => i.LineTotal);
                    return new CartSellerGroup(group.Key, sellerName, subtotal, 0, subtotal, group.ToList(), sellerType);
                })
                .ToList();

            var totals = _totalsCalculator.Calculate(sellerGroups);
            var promo = _promoCodeService.ApplyStored(httpContext, totals);
            return promo.Summary;
        }

        private async Task<Dictionary<string, (string Name, string SellerType)>> LoadSellerProfilesAsync(IEnumerable<string> sellerIds)
        {
            var profiles = new Dictionary<string, (string Name, string SellerType)>(StringComparer.OrdinalIgnoreCase);
            foreach (var sellerId in sellerIds)
            {
                if (string.IsNullOrWhiteSpace(sellerId) || profiles.ContainsKey(sellerId))
                {
                    continue;
                }

                var seller = await _userManager.FindByIdAsync(sellerId);
                var name = string.IsNullOrWhiteSpace(seller?.BusinessName) ? "Seller" : seller.BusinessName;
                var type = seller?.SellerType ?? string.Empty;
                profiles[sellerId] = (name, type);
            }

            return profiles;
        }

        private static ProductVariant? FindVariant(ProductModel product, IReadOnlyDictionary<string, string> attributes)
        {
            if (product.Variants == null || product.Variants.Count == 0 || attributes.Count == 0)
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

        private static string BuildVariantLabel(IReadOnlyDictionary<string, string> attributes)
        {
            if (attributes.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(", ", attributes.Select(kv => $"{kv.Key}: {kv.Value}"));
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
    }
}
