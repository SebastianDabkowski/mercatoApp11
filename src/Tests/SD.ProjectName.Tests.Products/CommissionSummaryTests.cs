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
    public class CommissionSummaryTests
    {
        private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        [Fact]
        public async Task GetCommissionSummaryAsync_UsesPayoutDateAndAggregatesPerSeller()
        {
            await using var context = CreateApplicationContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);

            var windowStart = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
            var windowEnd = new DateTimeOffset(2026, 2, 10, 23, 59, 59, TimeSpan.Zero);

            context.Orders.Add(CreateOrder("ORD-100", "seller-1", 100, 10, new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 3, 0, 0, 0, TimeSpan.Zero)));
            context.Orders.Add(CreateOrder("ORD-101", "seller-1", 50, 5, new DateTimeOffset(2026, 2, 5, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 6, 0, 0, 0, TimeSpan.Zero)));
            context.Orders.Add(CreateOrder("ORD-102", "seller-2", 60, 0, new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 2, 12, 0, 0, TimeSpan.Zero)));
            context.Orders.Add(CreateOrder("ORD-103", "seller-2", 80, 8, new DateTimeOffset(2026, 2, 2, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 12, 0, 0, 0, TimeSpan.Zero)));
            await context.SaveChangesAsync();

            var result = await service.GetCommissionSummaryAsync(windowStart, windowEnd);

            Assert.Equal(2, result.Count);

            var seller1 = result.Single(r => r.SellerId == "seller-1");
            Assert.Equal(2, seller1.OrderCount);
            Assert.Equal(150, seller1.GrossTotal);
            Assert.Equal(15, seller1.CommissionTotal);
            Assert.Equal(135, seller1.PayoutTotal);
            Assert.Equal(1, seller1.AdjustmentCount);

            var seller2 = result.Single(r => r.SellerId == "seller-2");
            Assert.Equal(1, seller2.OrderCount);
            Assert.Equal(60, seller2.GrossTotal);
            Assert.Equal(0, seller2.CommissionTotal);
            Assert.Equal(60, seller2.PayoutTotal);
            Assert.Equal(0, seller2.AdjustmentCount);
        }

        [Fact]
        public async Task GetCommissionSummaryDetailAsync_ReturnsOrdersWithinWindow()
        {
            await using var context = CreateApplicationContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);

            var windowStart = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
            var windowEnd = new DateTimeOffset(2026, 2, 10, 23, 59, 59, TimeSpan.Zero);

            context.Orders.Add(CreateOrder("ORD-200", "seller-3", 120, 12, new DateTimeOffset(2026, 1, 30, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero)));
            context.Orders.Add(CreateOrder("ORD-201", "seller-3", 40, 4, new DateTimeOffset(2026, 2, 4, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 4, 18, 0, 0, TimeSpan.Zero)));
            context.Orders.Add(CreateOrder("ORD-202", "seller-3", 70, 7, new DateTimeOffset(2026, 2, 5, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero)));
            await context.SaveChangesAsync();

            var detail = await service.GetCommissionSummaryDetailAsync(windowStart, windowEnd, "seller-3");

            Assert.NotNull(detail);
            Assert.Equal("seller-3", detail!.Summary.SellerId);
            Assert.Equal(2, detail.Summary.OrderCount);
            Assert.Equal(2, detail.Orders.Count);
            Assert.Contains(detail.Orders, o => o.IsAdjustment);
            Assert.DoesNotContain(detail.Orders, o => o.PayoutOn > windowEnd);
        }

        [Fact]
        public async Task ExportCommissionSummaryAsync_ReturnsAggregatedCsv()
        {
            await using var context = CreateApplicationContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);

            var windowStart = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
            var windowEnd = new DateTimeOffset(2026, 2, 5, 23, 59, 59, TimeSpan.Zero);

            context.Orders.Add(CreateOrder("ORD-300", "seller-9", 90, 9, windowStart, windowStart.AddDays(1)));
            context.Orders.Add(CreateOrder("ORD-301", "seller-10", 110, 11, windowStart.AddDays(1), windowStart.AddDays(2)));
            await context.SaveChangesAsync();

            var csv = await service.ExportCommissionSummaryAsync(windowStart, windowEnd);
            var content = Encoding.UTF8.GetString(csv);

            Assert.Contains("SellerId,SellerName,Orders,Gross,Commission,Payout,Adjustments,AdjustmentTotal,PeriodStart,PeriodEnd", content);
            Assert.Contains("seller-9", content);
            Assert.Contains("90.00", content);
            Assert.Contains("11.00", content);
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
            decimal gross,
            decimal commission,
            DateTimeOffset createdOn,
            DateTimeOffset payoutOn)
        {
            var items = new List<OrderItemDetail>
            {
                new(1, "Item", "Default", 1, gross, gross, sellerId, sellerId, OrderStatuses.Paid, "Category/Item")
            };
            var subOrder = new OrderSubOrder(
                $"{orderNumber}-01",
                sellerId,
                sellerId,
                gross,
                0,
                0,
                gross,
                items.Sum(i => i.Quantity),
                items,
                new OrderShippingDetail(sellerId, sellerId, "standard", "Standard", 0, "Standard shipping"),
                OrderStatuses.Paid,
                null,
                null,
                0);
            var payout = Math.Max(0, gross - commission);
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
                        gross,
                        commission,
                        payout,
                        0,
                        payout,
                        true,
                        new List<EscrowLedgerEntry>
                        {
                            new(subOrder.SubOrderNumber, sellerId, EscrowEntryTypes.PayoutEligible, payout, "eligible", payoutOn)
                        })
                },
                PaymentStatuses.Paid);

            return new OrderRecord
            {
                OrderNumber = orderNumber,
                Status = OrderStatuses.Paid,
                BuyerId = "buyer-1",
                BuyerName = "Buyer",
                BuyerEmail = "buyer@example.com",
                PaymentMethodId = "card",
                PaymentMethodLabel = "Card",
                ItemsSubtotal = gross,
                ShippingTotal = 0,
                GrandTotal = gross,
                TotalQuantity = subOrder.TotalQuantity,
                CreatedOn = createdOn,
                DetailsJson = JsonSerializer.Serialize(details, SerializerOptions),
                DeliveryAddressJson = "{}"
            };
        }
    }
}
