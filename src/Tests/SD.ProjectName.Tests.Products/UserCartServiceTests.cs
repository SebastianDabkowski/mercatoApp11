using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class UserCartServiceTests
    {
        [Fact]
        public async Task MergeOnSignIn_ShouldCombineGuestAndStoredCart()
        {
            var options = new CartOptions { CookieName = ".Test.Cart", MaxItems = 10, CookieLifespanDays = 7 };
            var cartService = new CartService(options, Mock.Of<ILogger<CartService>>());
            var user = new ApplicationUser { Id = "user-1" };
            user.CartData = SerializeCart(new List<CartItem>
            {
                new() { ProductId = 1, SellerId = "seller-1", Quantity = 1 }
            });

            var userManager = CreateUserManager(user);
            var service = CreateService(cartService, options, userManager, BuildProduct(1, "seller-1"));
            var httpContext = BuildContextWithCart(cartService, options.CookieName, new List<CartItem>
            {
                new() { ProductId = 1, SellerId = "seller-1", Quantity = 2 }
            });

            await service.MergeOnSignInAsync(httpContext, user);

            var mergedItems = cartService.GetItems(httpContext);
            var mergedItem = Assert.Single(mergedItems);
            Assert.Equal(3, mergedItem.Quantity);

            userManager.Verify(m => m.UpdateAsync(user), Times.Once);
            var stored = DeserializeCart(user.CartData!);
            var storedItem = Assert.Single(stored.Items);
            Assert.Equal(3, storedItem.Quantity);
        }

        [Fact]
        public async Task EnsureUserCart_ShouldLoadStoredCartWhenCookieEmpty()
        {
            var options = new CartOptions { CookieName = ".Test.Cart", MaxItems = 10, CookieLifespanDays = 7 };
            var cartService = new CartService(options, Mock.Of<ILogger<CartService>>());
            var user = new ApplicationUser { Id = "user-2", CartData = SerializeCart(new List<CartItem>
            {
                new() { ProductId = 5, SellerId = "seller-5", Quantity = 1 }
            }) };

            var userManager = CreateUserManager(user);
            userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);

            var service = CreateService(cartService, options, userManager, BuildProduct(5, "seller-5"));
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) }, "TestAuth"))
            };

            await service.EnsureUserCartAsync(httpContext);

            var items = cartService.GetItems(httpContext);
            Assert.Single(items);
            userManager.Verify(m => m.UpdateAsync(user), Times.Once);
        }

        private static UserCartService CreateService(CartService cartService, CartOptions options, Mock<UserManager<ApplicationUser>> userManager, params ProductModel[] products)
        {
            var repo = new Mock<IProductRepository>();
            repo.Setup(r => r.GetByIds(It.IsAny<IEnumerable<int>>(), false)).ReturnsAsync(products.ToList());
            var getProducts = new GetProducts(repo.Object);
            var totalsCalculator = new CartTotalsCalculator(options);
            var promoService = new PromoCodeService(new PromoOptions(), TimeProvider.System, Mock.Of<ILogger<PromoCodeService>>());
            var cartViewService = new CartViewService(cartService, totalsCalculator, getProducts, userManager.Object, promoService);
            return new UserCartService(cartService, cartViewService, userManager.Object, Mock.Of<ILogger<UserCartService>>());
        }

        private static Mock<UserManager<ApplicationUser>> CreateUserManager(ApplicationUser user)
        {
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

            userManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
            return userManager;
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

        private static string SerializeCart(List<CartItem> items)
        {
            return JsonSerializer.Serialize(new CartPayload(items), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        private static CartPayload DeserializeCart(string payload)
        {
            var cart = JsonSerializer.Deserialize<CartPayload>(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return cart ?? new CartPayload(new List<CartItem>());
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

        private static ProductModel BuildProduct(int id, string sellerId)
        {
            return new ProductModel
            {
                Id = id,
                SellerId = sellerId,
                Title = $"Product {id}",
                MerchantSku = $"SKU-{id}",
                Price = 10,
                Stock = 5,
                Category = "General",
                WorkflowState = ProductWorkflowStates.Active
            };
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
