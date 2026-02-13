using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;
using Xunit;

namespace SD.ProjectName.Tests.Products
{
    public class SellerOrderReportTests
    {
        private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        [Fact]
        public async Task GetSellerOrderReportAsync_FiltersByStatusAndDate()
        {
            await using var context = CreateApplicationContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);

            var inRange = CreateOrder("ORD-10", "seller-1", 100, 10, 10, new DateTimeOffset(2026, 2, 2, 10, 0, 0, TimeSpan.Zero), OrderStatuses.Paid, PaymentStatuses.Paid);
            var outOfStatus = CreateOrder("ORD-11", "seller-1", 80, 5, 8, new DateTimeOffset(2026, 2, 3, 12, 0, 0, TimeSpan.Zero), OrderStatuses.Shipped, PaymentStatuses.Paid);
            var otherSeller = CreateOrder("ORD-12", "seller-2", 50, 5, 5, new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero), OrderStatuses.Paid, PaymentStatuses.Paid);
            context.Orders.AddRange(inRange, outOfStatus, otherSeller);
            await context.SaveChangesAsync();

            var filters = new SellerOrderReportFilterOptions
            {
                FromDate = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                ToDate = new DateTimeOffset(2026, 2, 5, 23, 59, 59, TimeSpan.Zero),
                Statuses = new List<string> { OrderStatuses.Paid }
            };

            var result = await service.GetSellerOrderReportAsync("seller-1", filters, pageNumber: 1, pageSize: 20);

            Assert.Equal(1, result.TotalCount);
            Assert.Equal(1, result.TotalPages);
            var row = Assert.Single(result.Rows);
            Assert.Equal("ORD-10", row.OrderNumber);
            Assert.Equal(OrderStatuses.Paid, row.Status);
            Assert.Equal(PaymentStatuses.Paid, row.PaymentStatus);
            Assert.Equal(110, row.OrderValue);
            Assert.Equal(10, row.Commission);
            Assert.Equal(100, row.NetAmount);
            Assert.Equal(result.TotalOrderValue, row.OrderValue);
            Assert.Equal(result.TotalCommission, row.Commission);
            Assert.Equal(result.TotalNet, row.NetAmount);
        }

        [Fact]
        public async Task ExportSellerOrderReportAsync_UsesFiltersAndAmounts()
        {
            await using var context = CreateApplicationContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);

            var first = CreateOrder("ORD-20", "seller-1", 60, 5, 6, new DateTimeOffset(2026, 2, 4, 8, 0, 0, TimeSpan.Zero), OrderStatuses.Paid, PaymentStatuses.Paid);
            var second = CreateOrder("ORD-21", "seller-1", 40, 5, 4, new DateTimeOffset(2026, 2, 5, 10, 0, 0, TimeSpan.Zero), OrderStatuses.Paid, PaymentStatuses.Pending);
            var otherSeller = CreateOrder("ORD-22", "seller-2", 90, 5, 9, new DateTimeOffset(2026, 2, 4, 9, 0, 0, TimeSpan.Zero), OrderStatuses.Paid, PaymentStatuses.Paid);
            context.Orders.AddRange(first, second, otherSeller);
            await context.SaveChangesAsync();

            var export = await service.ExportSellerOrderReportAsync("seller-1", new SellerOrderReportFilterOptions
            {
                FromDate = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                ToDate = new DateTimeOffset(2026, 2, 10, 23, 59, 59, TimeSpan.Zero)
            });

            Assert.NotNull(export);
            Assert.False(export!.Truncated);
            Assert.Equal(2, export.RowCount);
            Assert.Equal(2, export.TotalMatching);

            var csv = Encoding.UTF8.GetString(export.Content);
            Assert.Contains("OrderNumber,SubOrderNumber,CreatedOn,Status,PaymentStatus,OrderValue,Commission,NetAmount", csv);
            Assert.Contains("ORD-20", csv);
            Assert.Contains("ORD-21", csv);
            Assert.DoesNotContain("ORD-22", csv);
            Assert.Contains("65.00", csv);
            Assert.Contains("6.00", csv);
            Assert.Contains("59.00", csv);
            Assert.Contains("44.00", csv);
        }

        [Fact]
        public async Task ExportSellerOrderReportAsync_ReturnsNullWhenNoMatches()
        {
            await using var context = CreateApplicationContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);

            var export = await service.ExportSellerOrderReportAsync("seller-1", new SellerOrderReportFilterOptions
            {
                FromDate = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                ToDate = new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero),
                Statuses = new List<string> { OrderStatuses.Delivered }
            });

            Assert.Null(export);
        }

        private static ApplicationDbContext CreateApplicationContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        private static OrderRecord CreateOrder(
            string orderNumber,
            string sellerId,
            decimal itemsSubtotal,
            decimal shipping,
            decimal commission,
            DateTimeOffset createdOn,
            string orderStatus,
            string paymentStatus)
        {
            var items = new List<OrderItemDetail>
            {
                new(1, "Item", "Default", 1, itemsSubtotal, itemsSubtotal, sellerId, sellerId, orderStatus, "Category/Item")
            };
            var grandTotal = itemsSubtotal + shipping;
            var subOrder = new OrderSubOrder(
                $"{orderNumber}-01",
                sellerId,
                sellerId,
                itemsSubtotal,
                shipping,
                0,
                grandTotal,
                items.Sum(i => i.Quantity),
                items,
                new OrderShippingDetail(sellerId, sellerId, "standard", "Standard", shipping, "Standard shipping"),
                orderStatus,
                null,
                null,
                0);
            var details = new OrderDetailsPayload(
                items,
                new List<OrderShippingDetail> { subOrder.ShippingDetail },
                subOrder.TotalQuantity,
                0,
                null,
                new List<OrderSubOrder> { subOrder },
                new List<EscrowAllocation>
                {
                    new(
                        subOrder.SubOrderNumber,
                        sellerId,
                        grandTotal,
                        commission,
                        grandTotal - commission,
                        0,
                        grandTotal - commission,
                        true,
                        new List<EscrowLedgerEntry>())
                },
                paymentStatus);
            return new OrderRecord
            {
                OrderNumber = orderNumber,
                Status = orderStatus,
                BuyerId = "buyer-1",
                BuyerName = "Buyer",
                BuyerEmail = "buyer@test.com",
                PaymentMethodId = "card",
                PaymentMethodLabel = "Card",
                ItemsSubtotal = itemsSubtotal,
                ShippingTotal = shipping,
                GrandTotal = grandTotal,
                TotalQuantity = subOrder.TotalQuantity,
                CreatedOn = createdOn,
                DetailsJson = JsonSerializer.Serialize(details, SerializerOptions),
                DeliveryAddressJson = "{}"
            };
        }
    }
}
