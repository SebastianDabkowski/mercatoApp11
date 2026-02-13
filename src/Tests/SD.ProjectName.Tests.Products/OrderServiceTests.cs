using System.Linq;
using System.Text;
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
            Assert.Equal("Buyer Four", sellerOrder.BuyerName);
            Assert.Equal("buyer4@example.com", sellerOrder.BuyerEmail);
            Assert.Equal(TestAddress.Phone, sellerOrder.BuyerPhone);
            Assert.Equal(OrderStatuses.Paid, sellerOrder.PaymentStatus);

            var sellerSummaries = await service.GetSummariesForSellerAsync("seller-2");
            Assert.Equal(1, sellerSummaries.TotalCount);
            var summary = Assert.Single(sellerSummaries.Items);
            Assert.Equal(result.Order.Id, summary.Id);
            Assert.Equal(OrderStatuses.Paid, summary.Status);
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

        [Fact]
        public async Task GetSummariesForBuyerAsync_ShouldFilterByStatusDateAndSeller()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;
            var buyerId = "buyer-filter";

            var first = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-f1", "sig-f1"),
                BuildQuote(),
                TestAddress,
                buyerId,
                "buyer-filter@example.com",
                "Buyer Filter",
                "Card",
                "card");
            first.Order.CreatedOn = now.AddDays(-10);

            var multi = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "express", ["seller-2"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-f2", "sig-f2"),
                BuildMultiSellerQuote(),
                TestAddress,
                buyerId,
                "buyer-filter@example.com",
                "Buyer Filter",
                "Card",
                "card");
            await service.UpdateSubOrderStatusAsync(multi.Order.Id, "seller-2", OrderStatuses.Shipped);
            multi.Order.CreatedOn = now.AddDays(-1);

            var recentOther = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-f3", "sig-f3"),
                BuildQuote(),
                TestAddress,
                buyerId,
                "buyer-filter@example.com",
                "Buyer Filter",
                "Card",
                "card");
            recentOther.Order.CreatedOn = now.AddDays(-2);

            await context.SaveChangesAsync();

            var filters = new BuyerOrderFilterOptions
            {
                Statuses = new List<string> { OrderStatuses.Shipped },
                FromDate = now.AddDays(-3),
                ToDate = now,
                SellerId = "seller-2"
            };

            var paged = await service.GetSummariesForBuyerAsync(buyerId, filters, 1, 10);

            Assert.Equal(1, paged.TotalCount);
            var summary = Assert.Single(paged.Items);
            Assert.Equal(multi.Order.Id, summary.Id);
            Assert.Equal(OrderStatuses.Shipped, summary.Status);
        }

        [Fact]
        public async Task GetSummariesForBuyerAsync_ShouldPaginateNewestFirst()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;
            var buyerId = "buyer-page";

            for (var i = 0; i < 12; i++)
            {
                var state = new CheckoutState("profile", TestAddress, now.AddMinutes(-i), new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, $"ref-p{i}", $"sig-p{i}");
                var result = await service.EnsureOrderAsync(state, BuildQuote(), TestAddress, buyerId, "buyer-page@example.com", "Buyer Page", "Card", "card");
                result.Order.CreatedOn = now.AddMinutes(-i);
            }

            await context.SaveChangesAsync();

            var paged = await service.GetSummariesForBuyerAsync(buyerId, null, 2, 5);

            Assert.Equal(12, paged.TotalCount);
            Assert.Equal(3, paged.TotalPages);
            Assert.Equal(5, paged.Items.Count);
            var expectedIds = context.Orders
                .Where(o => o.BuyerId == buyerId)
                .OrderByDescending(o => o.CreatedOn)
                .Skip(5)
                .Take(5)
                .Select(o => o.Id)
                .ToList();
            Assert.Equal(expectedIds, paged.Items.Select(i => i.Id).ToList());
        }

        [Fact]
        public async Task GetSummariesForSellerAsync_ShouldFilterByStatusDateAndBuyer()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;

            var older = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-sf1", "sig-sf1"),
                BuildQuote(),
                TestAddress,
                "buyer-sf-1",
                "alice@example.com",
                "Alice Buyer",
                "Card",
                "card");
            older.Order.CreatedOn = now.AddDays(-5);

            var match = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-sf2", "sig-sf2"),
                BuildQuote(),
                TestAddress,
                "buyer-sf-2",
                "bob@example.com",
                "Bob Buyer",
                "Card",
                "card");
            await service.UpdateSubOrderStatusAsync(match.Order.Id, "seller-1", OrderStatuses.Preparing);
            match.Order.CreatedOn = now.AddHours(-12);

            var differentStatus = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-sf3", "sig-sf3"),
                BuildQuote(),
                TestAddress,
                "buyer-sf-3",
                "bob@example.com",
                "Bob Buyer",
                "Card",
                "card");
            await service.UpdateSubOrderStatusAsync(differentStatus.Order.Id, "seller-1", OrderStatuses.Shipped);
            differentStatus.Order.CreatedOn = now.AddHours(-4);

            await context.SaveChangesAsync();

            var filters = new SellerOrderFilterOptions
            {
                Statuses = new List<string> { OrderStatuses.Preparing },
                FromDate = now.AddDays(-2),
                ToDate = now,
                BuyerQuery = "bob"
            };

            var paged = await service.GetSummariesForSellerAsync("seller-1", filters, 1, 10);

            Assert.Equal(1, paged.TotalCount);
            var summary = Assert.Single(paged.Items);
            Assert.Equal(match.Order.Id, summary.Id);
            Assert.Equal(OrderStatuses.Preparing, summary.Status);
            Assert.Equal("Bob Buyer", summary.BuyerName);
        }

        [Fact]
        public async Task GetSummariesForSellerAsync_ShouldPaginateNewestFirst()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;

            for (var i = 0; i < 12; i++)
            {
                var state = new CheckoutState("profile", TestAddress, now.AddMinutes(-i), new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, $"ref-sp{i}", $"sig-sp{i}");
                var result = await service.EnsureOrderAsync(state, BuildQuote(), TestAddress, $"buyer-sp-{i}", "seller-page@example.com", "Seller Page", "Card", "card");
                result.Order.CreatedOn = now.AddMinutes(-i);
            }

            await context.SaveChangesAsync();

            var paged = await service.GetSummariesForSellerAsync("seller-1", null, 2, 5);

            Assert.Equal(12, paged.TotalCount);
            Assert.Equal(3, paged.TotalPages);
            Assert.Equal(5, paged.Items.Count);
            var expectedIds = context.Orders
                .Where(o => o.DetailsJson.Contains("\"sellerId\":\"seller-1\""))
                .OrderByDescending(o => o.CreatedOn)
                .Skip(5)
                .Take(5)
                .Select(o => o.Id)
                .ToList();
            Assert.Equal(expectedIds, paged.Items.Select(i => i.Id).ToList());
        }

        [Fact]
        public async Task ExportSellerOrdersAsync_ShouldRespectFilters()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;

            var first = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-se1", "sig-se1"),
                BuildQuote(),
                TestAddress,
                "buyer-se-1",
                "export-one@example.com",
                "Export One",
                "Card",
                "card");
            first.Order.CreatedOn = now.AddDays(-1);

            var second = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-se2", "sig-se2"),
                BuildQuote(),
                TestAddress,
                "buyer-se-2",
                "export-two@example.com",
                "Export Two",
                "Card",
                "card");
            await service.UpdateSubOrderStatusAsync(second.Order.Id, "seller-1", OrderStatuses.Preparing);
            second.Order.CreatedOn = now;

            await context.SaveChangesAsync();

            var csvBytes = await service.ExportSellerOrdersAsync("seller-1", new SellerOrderFilterOptions
            {
                Statuses = new List<string> { OrderStatuses.Preparing },
                FromDate = now.AddDays(-2),
                ToDate = now.AddDays(1)
            });

            var csv = Encoding.UTF8.GetString(csvBytes);
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            var dataLine = lines[1].TrimEnd('\r');
            Assert.Contains(OrderStatuses.Preparing, dataLine);
            Assert.Contains(second.Order.OrderNumber, dataLine);
            Assert.Contains("export-two@example.com", dataLine);
            Assert.Contains("Standard", dataLine);
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
