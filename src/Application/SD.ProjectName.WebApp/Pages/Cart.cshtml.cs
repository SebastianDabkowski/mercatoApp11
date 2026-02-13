using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages
{
    public class CartModel : PageModel
    {
        private readonly CartService _cartService;
        private readonly GetProducts _getProducts;
        private readonly UserManager<ApplicationUser> _userManager;

        public List<CartSellerGroup> SellerGroups { get; private set; } = new();

        public CartModel(CartService cartService, GetProducts getProducts, UserManager<ApplicationUser> userManager)
        {
            _cartService = cartService;
            _getProducts = getProducts;
            _userManager = userManager;
        }

        public async Task OnGet()
        {
            var storedItems = _cartService.GetItems(HttpContext);
            if (storedItems.Count == 0)
            {
                return;
            }

            var productIds = storedItems.Select(i => i.ProductId).Distinct().ToList();
            var products = await _getProducts.GetByIds(productIds, includeDrafts: false);
            var productLookup = products.ToDictionary(p => p.Id);
            var validItems = new List<CartItem>();
            var displayItems = new List<CartDisplayItem>();

            foreach (var item in storedItems)
            {
                if (!productLookup.TryGetValue(item.ProductId, out var product))
                {
                    continue;
                }

                var variantLabel = string.Empty;
                decimal unitPrice;
                bool isAvailable;
                if (product.HasVariants)
                {
                    var variant = FindVariant(product, item.VariantAttributes);
                    if (variant == null)
                    {
                        continue;
                    }

                    unitPrice = variant.Price;
                    variantLabel = BuildVariantLabel(item.VariantAttributes);
                    isAvailable = variant.Stock > 0;
                }
                else
                {
                    unitPrice = product.Price;
                    isAvailable = product.Stock > 0;
                }

                validItems.Add(item);
                displayItems.Add(new CartDisplayItem(product, item.Quantity, variantLabel, unitPrice, unitPrice * item.Quantity, isAvailable));
            }

            if (validItems.Count != storedItems.Count)
            {
                _cartService.ReplaceCart(HttpContext, validItems);
            }

            var sellerNames = await LoadSellerNamesAsync(displayItems.Select(d => d.Product.SellerId).Distinct());
            SellerGroups = displayItems
                .GroupBy(d => d.Product.SellerId)
                .Select(group =>
                {
                    var sellerName = sellerNames.TryGetValue(group.Key, out var name) ? name : "Seller";
                    return new CartSellerGroup(group.Key, sellerName, group.ToList());
                })
                .ToList();
        }

        private async Task<Dictionary<string, string>> LoadSellerNamesAsync(IEnumerable<string> sellerIds)
        {
            var names = new Dictionary<string, string>();
            foreach (var sellerId in sellerIds)
            {
                if (string.IsNullOrWhiteSpace(sellerId) || names.ContainsKey(sellerId))
                {
                    continue;
                }

                var seller = await _userManager.FindByIdAsync(sellerId);
                names[sellerId] = string.IsNullOrWhiteSpace(seller?.BusinessName) ? "Seller" : seller.BusinessName;
            }

            return names;
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
    }

    public record CartSellerGroup(string SellerId, string SellerName, List<CartDisplayItem> Items);

    public record CartDisplayItem(ProductModel Product, int Quantity, string VariantLabel, decimal UnitPrice, decimal LineTotal, bool IsAvailable);
}
