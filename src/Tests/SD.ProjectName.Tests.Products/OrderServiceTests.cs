using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class OrderServiceTests
    {
        private static readonly DeliveryAddress TestAddress = new("Buyer One", "1 Market St", null, "Metropolis", "NY", "10001", "US", "555-1111");

        [Fact]
        public async Task EnsureOrderAsync_ShouldCreateOrder_AndSendEmail()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-1", "sig-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-1", "buyer@example.com", "Buyer One", "Card", "card");

            Assert.True(result.Created);
            Assert.Equal(1, context.Orders.Count());
            Assert.Equal(quote.Summary.GrandTotal, result.Order.GrandTotal);
            Assert.Equal(quote.Summary.TotalQuantity, result.Order.TotalQuantity);

            var view = await service.GetOrderAsync(result.Order.Id, "buyer-1");
            Assert.NotNull(view);
            Assert.Equal("Card", view!.PaymentMethodLabel);
            Assert.Single(view.Items);

            emailSender.Verify(e => e.SendEmailAsync("buyer@example.com", It.Is<string>(s => s.Contains(result.Order.OrderNumber)), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task EnsureOrderAsync_ShouldBeIdempotent_ForPaymentReference()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-2", "sig-2");

            var first = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-2", "buyer2@example.com", "Buyer Two", "Card", "card");
            var second = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-2", "buyer2@example.com", "Buyer Two", "Card", "card");

            Assert.True(first.Created);
            Assert.False(second.Created);
            Assert.Equal(first.Order.Id, second.Order.Id);
            Assert.Equal(1, context.Orders.Count());
            emailSender.Verify(e => e.SendEmailAsync("buyer2@example.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        private static ShippingQuote BuildQuote()
        {
            var product = new ProductModel { Id = 1, Title = "Sample Item", Price = 10, Stock = 5, SellerId = "seller-1" };
            var displayItem = new CartDisplayItem(product, 2, "Red", 10, 20, true, 5, new Dictionary<string, string>());
            var sellerGroup = new CartSellerGroup("seller-1", "Seller One", 20, 5, 25, new List<CartDisplayItem> { displayItem });
            var summary = new CartSummary(new List<CartSellerGroup> { sellerGroup }, 20, 5, 25, 2, CartSettlementSummary.Empty);
            var options = new List<ShippingMethodOption> { new("standard", "Standard", 5, "Standard delivery", true) };
            var sellerOptions = new List<SellerShippingOptions> { new("seller-1", "Seller One", options) };
            var selections = new Dictionary<string, string> { ["seller-1"] = "standard" };
            return new ShippingQuote(summary, sellerOptions, selections);
        }

        private static ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
