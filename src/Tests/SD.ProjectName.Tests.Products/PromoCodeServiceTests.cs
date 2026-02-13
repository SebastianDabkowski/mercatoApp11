using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SD.ProjectName.WebApp.Services;
using Moq;

namespace SD.ProjectName.Tests.Products
{
    public class PromoCodeServiceTests
    {
        [Fact]
        public void Apply_ShouldApplyPercentageDiscount()
        {
            var options = new PromoOptions
            {
                Codes = new List<PromoCodeRule>
                {
                    new()
                    {
                        Code = "SAVE10",
                        DiscountType = PromoDiscountType.Percentage,
                        Value = 0.1m
                    }
                }
            };

            var service = new PromoCodeService(options, TimeProvider.System, Mock.Of<ILogger<PromoCodeService>>());
            var httpContext = new DefaultHttpContext();
            var summary = BuildSummary(100, 5, ("seller-1", 100, 5));

            var result = service.Apply(httpContext, summary, "SAVE10");

            Assert.True(result.Success);
            Assert.Equal(10, result.Summary.DiscountTotal);
            Assert.Equal(95, result.Summary.GrandTotal);
            Assert.Equal("SAVE10", result.Summary.AppliedPromoCode);
        }

        [Fact]
        public void Apply_ShouldApplySellerScopedCode()
        {
            var options = new PromoOptions
            {
                Codes = new List<PromoCodeRule>
                {
                    new()
                    {
                        Code = "SELLER5",
                        DiscountType = PromoDiscountType.FixedAmount,
                        Value = 5,
                        SellerId = "seller-a"
                    }
                }
            };

            var service = new PromoCodeService(options, TimeProvider.System, Mock.Of<ILogger<PromoCodeService>>());
            var httpContext = new DefaultHttpContext();
            var summary = BuildSummary(
                50,
                0,
                ("seller-a", 20, 20),
                ("seller-b", 30, 30));

            var result = service.Apply(httpContext, summary, "SELLER5");

            Assert.True(result.Success);
            Assert.Equal(5, result.Summary.DiscountTotal);
            Assert.Equal(45, result.Summary.GrandTotal);
        }

        [Fact]
        public void Apply_ShouldRejectSecondCodeWhenOneIsActive()
        {
            var options = new PromoOptions
            {
                Codes = new List<PromoCodeRule>
                {
                    new() { Code = "SAVE10", DiscountType = PromoDiscountType.Percentage, Value = 0.1m },
                    new() { Code = "TAKE5", DiscountType = PromoDiscountType.FixedAmount, Value = 5 }
                }
            };

            var service = new PromoCodeService(options, TimeProvider.System, Mock.Of<ILogger<PromoCodeService>>());
            var httpContext = new DefaultHttpContext();
            var summary = BuildSummary(50, 0, ("seller-1", 50, 0));

            var first = service.Apply(httpContext, summary, "SAVE10");
            CopyPromoCookie(httpContext, options.CookieName);
            var second = service.Apply(httpContext, summary, "TAKE5");

            Assert.True(first.Success);
            Assert.False(second.Success);
            Assert.True(second.AlreadyApplied);
            Assert.Equal("SAVE10", second.AppliedCode);
            Assert.Equal(5, Math.Round(first.Summary.DiscountTotal, 2));
        }

        private static CartSummary BuildSummary(decimal itemsSubtotal, decimal shippingTotal, params (string SellerId, decimal Subtotal, decimal Shipping)[] sellers)
        {
            var groups = new List<CartSellerGroup>();
            foreach (var seller in sellers)
            {
                var total = seller.Subtotal + seller.Shipping;
                groups.Add(new CartSellerGroup(seller.SellerId, "Seller", seller.Subtotal, seller.Shipping, total, new List<CartDisplayItem>()));
            }

            return new CartSummary(groups, itemsSubtotal, shippingTotal, itemsSubtotal + shippingTotal, 1, CartSettlementSummary.Empty);
        }

        private static void CopyPromoCookie(DefaultHttpContext context, string cookieName)
        {
            var header = context.Response.Headers["Set-Cookie"].ToString();
            var prefix = $"{cookieName}=";
            var start = header.LastIndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return;
            }

            var end = header.IndexOf(';', start);
            var value = end > start ? header[(start + prefix.Length)..end] : header[(start + prefix.Length)..];
            context.Request.Headers["Cookie"] = $"{cookieName}={value}";
        }
    }
}
