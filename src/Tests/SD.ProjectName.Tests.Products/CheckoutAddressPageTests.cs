using System;
using System.Collections;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Pages.Checkout;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class CheckoutAddressPageTests
    {
        [Fact]
        public async Task OnGet_ShouldPreferBuyerSavedAddressOverStaleState()
        {
            var cartOptions = new CartOptions
            {
                CookieName = ".Test.Cart",
                MaxItems = 10,
                CookieLifespanDays = 7,
                DefaultShippingBase = 0,
                DefaultShippingPerItem = 0,
                PlatformCommissionRate = 0
            };
            var cartService = new CartService(cartOptions, Mock.Of<ILogger<CartService>>());
            var product = new ProductModel
            {
                Id = 1,
                SellerId = "seller-1",
                Title = "Test product",
                MerchantSku = "SKU-1",
                Price = 10,
                Stock = 2,
                Category = "General",
                WorkflowState = ProductWorkflowStates.Active, ModerationStatus = ProductModerationStatuses.Approved
            };

            var context = BuildContextWithCart(cartService, cartOptions.CookieName, new List<CartItem>
            {
                new()
                {
                    ProductId = product.Id,
                    SellerId = product.SellerId,
                    Quantity = 1,
                    VariantAttributes = new Dictionary<string, string>()
                }
            });

            var productRepository = new Mock<IProductRepository>();
            productRepository.Setup(r => r.GetByIds(It.IsAny<IEnumerable<int>>(), false))
                .ReturnsAsync(new List<ProductModel> { product });
            var getProducts = new GetProducts(productRepository.Object);
            var totalsCalculator = new CartTotalsCalculator(cartOptions);
            var promoService = new PromoCodeService(
                new PromoOptions { CookieName = ".Test.Promo", Codes = new List<PromoCodeRule>() },
                TimeProvider.System,
                Mock.Of<ILogger<PromoCodeService>>());

            var buyer = new ApplicationUser { Id = "buyer-1", UserName = "buyer@example.com" };
            var seller = new ApplicationUser { Id = product.SellerId, BusinessName = "Seller" };
            var userManager = CreateUserManager(buyer, seller);

            var cartViewService = new CartViewService(cartService, totalsCalculator, getProducts, userManager.Object, promoService);
            var userCartService = new Mock<IUserCartService>();
            userCartService.Setup(s => s.EnsureUserCartAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await using var dbContext = new ApplicationDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            var shippingAddressService = new ShippingAddressService(dbContext, new ShippingAddressOptions());
            var savedAddress = await shippingAddressService.UpsertAsync(buyer.Id, new AddressForm
            {
                Recipient = "Buyer One",
                Line1 = "123 Main St",
                City = "Springfield",
                State = "IL",
                PostalCode = "62701",
                Country = "US",
                Phone = "555-1111"
            }, makeDefault: true);

            var checkoutOptions = new CheckoutOptions { CookieName = ".Test.Checkout" };
            var checkoutStateService = new CheckoutStateService(checkoutOptions);
            var staleState = new CheckoutState(
                "stale-key",
                new DeliveryAddress("Other Person", "Old St", null, "Oldtown", "CA", "90000", "CA", "999"),
                DateTimeOffset.UtcNow);
            var serializedState = JsonSerializer.Serialize(staleState, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            AddCookie(context, checkoutOptions.CookieName, serializedState);
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, buyer.Id) }, "Test"));

            var pageContext = new PageContext(new ActionContext(context, new RouteData(), new PageActionDescriptor()));
            var model = new AddressModel(cartViewService, userCartService.Object, checkoutStateService, shippingAddressService, userManager.Object)
            {
                PageContext = pageContext
            };

            var result = await model.OnGetAsync();

            Assert.IsType<PageResult>(result);
            Assert.Equal(savedAddress.Id.ToString(), model.Input.SavedAddressKey);
            Assert.Equal(savedAddress.Recipient, model.Input.NewAddress.Recipient);
            Assert.Equal(savedAddress.Line1, model.Input.NewAddress.Line1);
            Assert.Equal(savedAddress.City, model.Input.NewAddress.City);
            Assert.Equal(savedAddress.PostalCode, model.Input.NewAddress.PostalCode);
            Assert.Equal(savedAddress.Country, model.Input.NewAddress.Country);
            Assert.Equal(savedAddress.Phone, model.Input.NewAddress.Phone);
        }

        private static Mock<UserManager<ApplicationUser>> CreateUserManager(ApplicationUser buyer, ApplicationUser seller)
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

            userManager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
            userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(buyer);
            userManager.Setup(m => m.FindByIdAsync(buyer.Id)).ReturnsAsync(buyer);
            userManager.Setup(m => m.FindByIdAsync(seller.Id)).ReturnsAsync(seller);
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

        private static void AddCookie(HttpContext context, string cookieName, string value)
        {
            var cookies = context.Request.Cookies.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            cookies[cookieName] = value;
            context.Features.Set<IRequestCookiesFeature>(new RequestCookiesFeature(new TestCookieCollection(cookies)));
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
