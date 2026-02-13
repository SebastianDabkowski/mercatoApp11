using System.Collections;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class CartServiceTests
    {
        [Fact]
        public void AddProduct_ShouldKeepItemsFromDifferentSellers()
        {
            var options = new CartOptions { CookieName = ".Test.Cart", MaxItems = 10, CookieLifespanDays = 7 };
            var service = new CartService(options, Mock.Of<ILogger<CartService>>());

            var firstContext = new DefaultHttpContext();
            service.AddProduct(firstContext, BuildProduct(10, "seller-1"), null, 1);
            var firstPayload = ExtractCookiePayload(firstContext, options.CookieName);

            var secondContext = BuildContextWithCookie(options.CookieName, firstPayload);
            service.AddProduct(secondContext, BuildProduct(20, "seller-2"), null, 1);
            var updated = ExtractPayload(secondContext, options.CookieName);

            Assert.Equal(2, updated.Items.Count);
            Assert.Contains(updated.Items, i => i.ProductId == 10 && i.SellerId == "seller-1");
            Assert.Contains(updated.Items, i => i.ProductId == 20 && i.SellerId == "seller-2");
        }

        [Fact]
        public void AddProduct_ShouldIncrementQuantityForSameProductAndVariant()
        {
            var options = new CartOptions { CookieName = ".Test.Cart", MaxItems = 10, CookieLifespanDays = 7 };
            var service = new CartService(options, Mock.Of<ILogger<CartService>>());
            var product = BuildProduct(5, "seller-1", withVariant: true);
            var attributes = new Dictionary<string, string> { { "Color", "Red" } };

            var firstContext = new DefaultHttpContext();
            service.AddProduct(firstContext, product, attributes, 1);
            var firstPayload = ExtractCookiePayload(firstContext, options.CookieName);

            var secondContext = BuildContextWithCookie(options.CookieName, firstPayload);
            service.AddProduct(secondContext, product, attributes, 1);
            var updated = ExtractPayload(secondContext, options.CookieName);

            var item = Assert.Single(updated.Items);
            Assert.Equal(2, item.Quantity);
            Assert.Equal("seller-1", item.SellerId);
        }

        private static ProductModel BuildProduct(int id, string sellerId, bool withVariant = false)
        {
            var product = new ProductModel
            {
                Id = id,
                SellerId = sellerId,
                Title = $"Product {id}",
                MerchantSku = $"SKU-{id}",
                Price = 10,
                Stock = 5,
                Category = "General",
                WorkflowState = ProductWorkflowStates.Active, ModerationStatus = ProductModerationStatuses.Approved
            };

            if (withVariant)
            {
                product.HasVariants = true;
                product.Variants = new List<ProductVariant>
                {
                    new ProductVariant
                    {
                        Sku = "VAR-1",
                        Price = 12,
                        Stock = 3,
                        Attributes = new Dictionary<string, string>
                        {
                            { "Color", "Red" }
                        }
                    }
                };
            }

            return product;
        }

        private static CartPayload ExtractPayload(HttpContext context, string cookieName)
        {
            var json = ExtractCookiePayload(context, cookieName);
            var payload = JsonSerializer.Deserialize<CartPayload>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return payload ?? new CartPayload(new List<CartItem>());
        }

        private static string ExtractCookiePayload(HttpContext context, string cookieName)
        {
            var header = context.Response.Headers["Set-Cookie"].ToString();
            var prefix = $"{cookieName}=";
            var start = header.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            var end = header.IndexOf(';', start);
            var encoded = end > start ? header[(start + prefix.Length)..end] : header[(start + prefix.Length)..];
            return Uri.UnescapeDataString(encoded);
        }

        private static HttpContext BuildContextWithCookie(string cookieName, string payload)
        {
            var context = new DefaultHttpContext();
            context.Features.Set<IRequestCookiesFeature>(
                new RequestCookiesFeature(new TestCookieCollection(new Dictionary<string, string>
                {
                    { cookieName, payload }
                })));
            return context;
        }

        private sealed class TestCookieCollection : IRequestCookieCollection
        {
            private readonly Dictionary<string, string> _cookies;

            public TestCookieCollection(Dictionary<string, string> cookies)
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
    }
}
