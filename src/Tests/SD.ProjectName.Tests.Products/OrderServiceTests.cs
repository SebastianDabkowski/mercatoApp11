using System.Linq;
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
            Assert.Equal(OrderStatuses.Paid, result.Order.Status);

            var view = await service.GetOrderAsync(result.Order.Id, "buyer-1");
            Assert.NotNull(view);
            Assert.Equal("Card", view!.PaymentMethodLabel);
            Assert.Single(view.Items);
            Assert.Single(view.SubOrders);
            Assert.Equal(view.GrandTotal, view.SubOrders.First().GrandTotal);
            Assert.Equal(view.SubOrders.First().TotalQuantity, view.TotalQuantity);
            Assert.Equal(OrderStatuses.Paid, view.Status);
            Assert.All(view.SubOrders, s => Assert.Equal(OrderStatuses.Paid, s.Status));

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

        [Fact]
        public async Task EnsureOrderAsync_ShouldSplitIntoSellerSubOrders()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildMultiSellerQuote();
            var selections = new Dictionary<string, string> { ["seller-1"] = "express", ["seller-2"] = "standard" };
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, selections, "card", CheckoutPaymentStatus.Confirmed, "ref-3", "sig-3");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-3", "buyer3@example.com", "Buyer Three", "Card", "card");

            Assert.True(result.Created);
            var view = await service.GetOrderAsync(result.Order.Id, "buyer-3");
            Assert.NotNull(view);
            Assert.Equal(2, view!.SubOrders.Count);
            Assert.Equal(view.SubOrders.Sum(s => s.GrandTotal), view.GrandTotal);
            var firstSeller = view.SubOrders.First(s => s.SellerId == "seller-1");
            Assert.Equal("express", firstSeller.ShippingDetail.MethodId);
            Assert.Equal(7, firstSeller.Shipping);
            Assert.True(firstSeller.Items.All(i => i.SellerId == "seller-1"));
            Assert.Equal(OrderStatuses.Paid, firstSeller.Status);
            var secondSeller = view.SubOrders.First(s => s.SellerId == "seller-2");
            Assert.Equal("standard", secondSeller.ShippingDetail.MethodId);
            Assert.Equal(5, secondSeller.Shipping);
            Assert.True(secondSeller.Items.All(i => i.SellerId == "seller-2"));
            Assert.Equal(OrderStatuses.Paid, secondSeller.Status);
        }

        [Fact]
        public async Task GetSellerOrderAsync_ShouldReturnOnlySellerItems()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildMultiSellerQuote();
            var selections = new Dictionary<string, string> { ["seller-1"] = "express", ["seller-2"] = "standard" };
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, selections, "card", CheckoutPaymentStatus.Confirmed, "ref-4", "sig-4");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-4", "buyer4@example.com", "Buyer Four", "Card", "card");
            var sellerOrder = await service.GetSellerOrderAsync(result.Order.Id, "seller-2");

            Assert.NotNull(sellerOrder);
            Assert.Equal("seller-2", sellerOrder!.Shipping.SellerId);
            Assert.True(sellerOrder.Items.All(i => i.SellerId == "seller-2"));
            Assert.Equal(sellerOrder.ShippingTotal + sellerOrder.ItemsSubtotal - sellerOrder.DiscountTotal, sellerOrder.GrandTotal);
            Assert.Equal(OrderStatuses.Paid, sellerOrder.Status);

            var sellerSummaries = await service.GetSummariesForSellerAsync("seller-2");
            Assert.Single(sellerSummaries);
            Assert.Equal(result.Order.Id, sellerSummaries[0].Id);
            Assert.Equal(OrderStatuses.Paid, sellerSummaries[0].Status);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldEnforceTransitions_AndPersist()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-10", "sig-10");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-10", "buyer10@example.com", "Buyer Ten", "Card", "card");
            Assert.Equal(OrderStatuses.Paid, result.Order.Status);

            var preparing = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Preparing, null, null);
            Assert.True(preparing.Success);
            Assert.Equal(OrderStatuses.Preparing, preparing.UpdatedSubOrder!.Status);
            Assert.Equal(OrderStatuses.Preparing, preparing.OrderStatus);

            var shipped = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Shipped, "TRACK123", null);
            Assert.True(shipped.Success);
            Assert.Equal("TRACK123", shipped.UpdatedSubOrder!.TrackingNumber);
            Assert.Equal(OrderStatuses.Shipped, shipped.OrderStatus);

            var delivered = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered, "TRACK123", null);
            Assert.True(delivered.Success);
            Assert.Equal(OrderStatuses.Delivered, delivered.UpdatedSubOrder!.Status);
            Assert.Equal(OrderStatuses.Delivered, delivered.OrderStatus);

            var sellerOrder = await service.GetSellerOrderAsync(result.Order.Id, "seller-1");
            Assert.NotNull(sellerOrder);
            Assert.Equal(OrderStatuses.Delivered, sellerOrder!.Status);
            Assert.Equal("TRACK123", sellerOrder.TrackingNumber);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldRejectInvalidTransition()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-11", "sig-11");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-11", "buyer11@example.com", "Buyer Eleven", "Card", "card");

            var rejection = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Preparing);
            Assert.True(rejection.Success);

            var invalid = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Paid);
            Assert.False(invalid.Success);
            Assert.NotNull(invalid.Error);
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

        private static ShippingQuote BuildMultiSellerQuote()
        {
            var productOne = new ProductModel { Id = 1, Title = "Red Shirt", Price = 10, Stock = 5, SellerId = "seller-1" };
            var productTwo = new ProductModel { Id = 2, Title = "Blue Jeans", Price = 20, Stock = 5, SellerId = "seller-2" };
            var itemOne = new CartDisplayItem(productOne, 1, string.Empty, 10, 10, true, 5, new Dictionary<string, string>());
            var itemTwo = new CartDisplayItem(productTwo, 2, "32", 20, 40, true, 5, new Dictionary<string, string>());

            var sellerOneGroup = new CartSellerGroup("seller-1", "Seller One", 10, 7, 17, new List<CartDisplayItem> { itemOne });
            var sellerTwoGroup = new CartSellerGroup("seller-2", "Seller Two", 40, 5, 45, new List<CartDisplayItem> { itemTwo });
            var summary = new CartSummary(new List<CartSellerGroup> { sellerOneGroup, sellerTwoGroup }, 50, 12, 62, 3, CartSettlementSummary.Empty);

            var sellerOneOptions = new List<ShippingMethodOption> { new("standard", "Standard", 5, "Standard", false), new("express", "Express", 7, "Express", true) };
            var sellerTwoOptions = new List<ShippingMethodOption> { new("standard", "Standard", 5, "Standard", true) };
            var sellerOptions = new List<SellerShippingOptions>
            {
                new("seller-1", "Seller One", sellerOneOptions),
                new("seller-2", "Seller Two", sellerTwoOptions)
            };
            var selections = new Dictionary<string, string> { ["seller-1"] = "express", ["seller-2"] = "standard" };

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
