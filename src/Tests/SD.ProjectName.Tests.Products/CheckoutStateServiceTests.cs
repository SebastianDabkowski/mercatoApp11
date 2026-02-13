using Microsoft.AspNetCore.Http;
using Xunit;
using SD.ProjectName.WebApp.Pages.Checkout;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class CheckoutStateServiceTests
    {
        [Fact]
        public void SaveAndGet_ShouldRoundTripState()
        {
            var options = new CheckoutOptions
            {
                CookieName = ".Test.Checkout",
                StateLifespanDays = 2
            };
            var service = new CheckoutStateService(options);
            var address = new DeliveryAddress("John Doe", "123 Main St", null, "Springfield", "IL", "12345", "US", "5551234");

            var responseContext = new DefaultHttpContext();
            service.Save(responseContext, "profile", address);

            var cookieHeader = responseContext.Response.Headers["Set-Cookie"].ToString();
            var cookieValue = ExtractCookieValue(options.CookieName, cookieHeader);

            var requestContext = new DefaultHttpContext();
            requestContext.Request.Headers["Cookie"] = $"{options.CookieName}={cookieValue}";

            var state = service.Get(requestContext);

            Assert.NotNull(state);
            Assert.Equal("profile", state!.SavedAddressKey);
            Assert.Equal(address, state.Address);
        }

        [Fact]
        public void ShippingRegionHelper_ShouldDetectBlockedSellers()
        {
            var sellerGroups = new List<CartSellerGroup>
            {
                new("seller-1", "Local Seller", 0, 0, 0, new List<CartDisplayItem>()),
                new("seller-2", "Remote Seller", 0, 0, 0, new List<CartDisplayItem>())
            };

            var countries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["seller-1"] = "US",
                ["seller-2"] = "CA"
            };

            var blocked = ShippingRegionHelper.FindBlockedSellers(sellerGroups, countries, "US");

            Assert.Single(blocked);
            Assert.Contains("Remote Seller", blocked);
        }

        private static string ExtractCookieValue(string cookieName, string setCookieHeader)
        {
            var prefix = $"{cookieName}=";
            var start = setCookieHeader.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            var end = setCookieHeader.IndexOf(';', start);
            return end > start
                ? setCookieHeader[(start + prefix.Length)..end]
                : setCookieHeader[(start + prefix.Length)..];
        }
    }
}
