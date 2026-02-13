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

        public CartModel(GetProducts getProducts, CartService cartService)
        {
            _getProducts = getProducts;
            _cartService = cartService;
        }

        public async Task<IActionResult> OnPostAddAsync([FromBody] AddToCartRequest request)
        {
            if (request == null || request.ProductId <= 0)
            {
                return BadRequest(new { message = "Invalid product." });
            }

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

            var result = _cartService.AddProduct(HttpContext, product, attributes, request.Quantity);
            var message = result.Status == CartUpdateStatus.Added ? "Added to cart." : "Quantity updated.";

            return new JsonResult(new AddToCartResponse(true, message, result.Quantity, result.Status.ToString().ToLowerInvariant()));
        }

        private static ProductVariant? FindVariant(ProductModel product, IDictionary<string, string> attributes)
        {
            if (product.Variants == null || product.Variants.Count == 0)
            {
                return null;
            }

            return product.Variants.FirstOrDefault(v => AttributesMatch(v.Attributes, attributes));
        }

        private static bool AttributesMatch(IDictionary<string, string> variantAttributes, IDictionary<string, string> requested)
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

        private static Dictionary<string, string> NormalizeAttributes(Dictionary<string, string>? attributes)
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

    public record AddToCartRequest
    {
        public int ProductId { get; init; }

        public int Quantity { get; init; } = 1;

        public Dictionary<string, string>? VariantAttributes { get; init; }
    }

    public record AddToCartResponse(bool Success, string Message, int Quantity, string Status);
}
