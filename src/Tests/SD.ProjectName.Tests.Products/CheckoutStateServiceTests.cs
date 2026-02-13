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
        public void SaveShippingAndPayment_ShouldPersistSelections()
        {
            var options = new CheckoutOptions
            {
                CookieName = ".Test.Checkout"
            };
            var service = new CheckoutStateService(options);
            var address = new DeliveryAddress("John Doe", "123 Main St", null, "Springfield", "IL", "12345", "US", "5551234");

            var responseContext = new DefaultHttpContext();
            service.Save(responseContext, "profile", address);

            var cookieValue = ExtractCookieValue(options.CookieName, responseContext.Response.Headers["Set-Cookie"].ToString());
            var shippingContext = BuildRequestContext(options.CookieName, cookieValue);
            service.SaveShippingSelections(shippingContext, new Dictionary<string, string> { ["seller-1"] = "express" });

            cookieValue = ExtractCookieValue(options.CookieName, shippingContext.Response.Headers["Set-Cookie"].ToString());
            var paymentContext = BuildRequestContext(options.CookieName, cookieValue);
            service.SavePaymentSelection(paymentContext, "card", CheckoutPaymentStatus.Pending, "ref-1");

            cookieValue = ExtractCookieValue(options.CookieName, paymentContext.Response.Headers["Set-Cookie"].ToString());
            var requestContext = BuildRequestContext(options.CookieName, cookieValue);

            var state = service.Get(requestContext);

            Assert.NotNull(state);
            Assert.NotNull(state!.ShippingSelections);
            Assert.Equal("express", state.ShippingSelections["seller-1"]);
            Assert.Equal("card", state.PaymentMethod);
            Assert.Equal(CheckoutPaymentStatus.Pending, state.PaymentStatus);
            Assert.Equal("ref-1", state.PaymentReference);
        }

        [Fact]
        public void SavingNewAddress_ShouldResetShippingAndPayment()
        {
            var options = new CheckoutOptions
            {
                CookieName = ".Test.Checkout"
            };
            var service = new CheckoutStateService(options);
            var address = new DeliveryAddress("Jane Doe", "123 Main St", null, "Springfield", "IL", "12345", "US", "5551234");
            var updatedAddress = new DeliveryAddress("Jane Doe", "22 Oak St", null, "Springfield", "IL", "55555", "US", "5551234");

            HttpContext context = new DefaultHttpContext();
            service.Save(context, "profile", address);
            var cookieValue = ExtractCookieValue(options.CookieName, context.Response.Headers["Set-Cookie"].ToString());

            var shippingContext = BuildRequestContext(options.CookieName, cookieValue);
            service.SaveShippingSelections(shippingContext, new Dictionary<string, string> { ["seller-1"] = "standard" });

            cookieValue = ExtractCookieValue(options.CookieName, shippingContext.Response.Headers["Set-Cookie"].ToString());
            var paymentContext = BuildRequestContext(options.CookieName, cookieValue);
            service.SavePaymentSelection(paymentContext, "card", CheckoutPaymentStatus.Confirmed, "ref-123");

            cookieValue = ExtractCookieValue(options.CookieName, paymentContext.Response.Headers["Set-Cookie"].ToString());
            context = BuildRequestContext(options.CookieName, cookieValue);

            service.Save(context, "profile", updatedAddress);
            cookieValue = ExtractCookieValue(options.CookieName, context.Response.Headers["Set-Cookie"].ToString());
            var stateContext = BuildRequestContext(options.CookieName, cookieValue);
            var state = service.Get(stateContext);

            Assert.NotNull(state);
            Assert.NotNull(state!.ShippingSelections);
            Assert.Empty(state.ShippingSelections);
            Assert.Null(state.PaymentMethod);
            Assert.Equal(CheckoutPaymentStatus.None, state.PaymentStatus);
            Assert.Null(state.PaymentReference);
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
            var start = setCookieHeader.LastIndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            var end = setCookieHeader.IndexOf(';', start);
            return end > start
                ? setCookieHeader[(start + prefix.Length)..end]
                : setCookieHeader[(start + prefix.Length)..];
        }

        private static HttpContext BuildRequestContext(string cookieName, string cookieValue)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Cookie"] = $"{cookieName}={cookieValue}";
            return context;
        }
    }
}
