using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class CartViewServiceTests
    {
        [Fact]
        public async Task BuildAsync_ShouldClampQuantitiesToAvailableStock()
        {
            var cartOptions = new CartOptions { CookieName = ".Test.Cart", MaxItems = 10, CookieLifespanDays = 7 };
            var cartService = new CartService(cartOptions, Mock.Of<ILogger<CartService>>());
            var product = new ProductModel
            {
                Id = 1,
                SellerId = "seller-1",
                Title = "Test product",
                MerchantSku = "SKU-1",
                Price = 20,
                Stock = 2,
                Category = "General",
                WorkflowState = ProductWorkflowStates.Active
            };

            var httpContext = BuildContextWithCart(cartService, cartOptions.CookieName, new List<CartItem>
            {
                new()
                {
                    ProductId = product.Id,
                    SellerId = product.SellerId,
                    Quantity = 5,
                    VariantAttributes = new Dictionary<string, string>()
                }
            });

            var service = CreateService(cartService, product);

            var summary = await service.BuildAsync(httpContext);

            var group = Assert.Single(summary.SellerGroups);
            var item = Assert.Single(group.Items);
            Assert.Equal(2, item.Quantity);
            Assert.Equal(40, summary.GrandTotal);
            Assert.Equal(2, summary.TotalQuantity);
            Assert.Equal(2, ExtractCartPayload(httpContext, cartOptions.CookieName)?.Items.Single().Quantity);
        }

        [Fact]
        public async Task BuildAsync_ShouldRemoveUnavailableVariant()
        {
            var cartOptions = new CartOptions { CookieName = ".Test.Cart", MaxItems = 10, CookieLifespanDays = 7 };
            var cartService = new CartService(cartOptions, Mock.Of<ILogger<CartService>>());
            var product = new ProductModel
            {
                Id = 10,
                SellerId = "seller-1",
                Title = "Variant product",
                MerchantSku = "SKU-VAR",
                Category = "General",
                WorkflowState = ProductWorkflowStates.Active,
                HasVariants = true,
                Variants = new List<ProductVariant>
                {
                    new()
                    {
                        Sku = "BLUE",
                        Price = 15,
                        Stock = 3,
                        Attributes = new Dictionary<string, string> { { "Color", "Blue" } }
                    }
                }
            };

            var httpContext = BuildContextWithCart(cartService, cartOptions.CookieName, new List<CartItem>
            {
                new()
                {
                    ProductId = product.Id,
                    SellerId = product.SellerId,
                    Quantity = 1,
                    VariantAttributes = new Dictionary<string, string> { { "Color", "Red" } }
                }
            });

            var service = CreateService(cartService, product);

            var summary = await service.BuildAsync(httpContext);

            Assert.True(summary.IsEmpty);
            Assert.Empty(ExtractCartPayload(httpContext, cartOptions.CookieName)?.Items ?? new List<CartItem>());
        }

        private static CartViewService CreateService(CartService cartService, ProductModel product)
        {
            var repo = new Mock<IProductRepository>();
            repo.Setup(r => r.GetByIds(It.IsAny<IEnumerable<int>>(), false)).ReturnsAsync(new List<ProductModel> { product });
            var getProducts = new GetProducts(repo.Object);

            var store = new Mock<IUserStore<ApplicationUser>>();
            var userManager = new Mock<UserManager<ApplicationUser>>(
                store.Object,
                null!,
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!,
                Mock.Of<ILogger<UserManager<ApplicationUser>>>());

            userManager.Setup(u => u.FindByIdAsync(product.SellerId))
                .ReturnsAsync(new ApplicationUser { Id = product.SellerId, BusinessName = "Seller" });

            return new CartViewService(cartService, getProducts, userManager.Object);
        }

        private static HttpContext BuildContextWithCart(CartService cartService, string cookieName, List<CartItem> items)
        {
            var context = new DefaultHttpContext();
            cartService.ReplaceCart(context, items);
            var payload = ExtractCookieValue(context, cookieName);
            context.Features.Set<IRequestCookiesFeature>(
                new RequestCookiesFeature(new TestCookieCollection(new Dictionary<string, string>
                {
                    { cookieName, payload }
                })));
            return context;
        }

        private static CartPayload? ExtractCartPayload(HttpContext context, string cookieName)
        {
            var value = ExtractCookieValue(context, cookieName);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return System.Text.Json.JsonSerializer.Deserialize<CartPayload>(value, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
        }

        private static string ExtractCookieValue(HttpContext context, string cookieName)
        {
            var header = context.Response.Headers["Set-Cookie"].ToString();
            var prefix = $"{cookieName}=";
            var start = header.LastIndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            var end = header.IndexOf(';', start);
            var encoded = end > start ? header[(start + prefix.Length)..end] : header[(start + prefix.Length)..];
            return Uri.UnescapeDataString(encoded);
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

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _cookies.GetEnumerator();
        }
    }
}
