using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;
using Xunit;

namespace SD.ProjectName.Tests.Products
{
    public class AdminReportingServiceTests
    {
        private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        [Fact]
        public async Task GetOrderReportAsync_FiltersBySellerStatusAndPayment()
        {
            await using var appContext = CreateApplicationContext();
            await using var productContext = CreateProductContext();

            var paid = CreateOrder("ORD-1", "seller-1", 100, 10, 10, new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero), OrderStatuses.Paid, PaymentStatuses.Paid, "Buyer One", "buyer1@test.com");
            var pending = CreateOrder("ORD-2", "seller-1", 50, 5, 5, new DateTimeOffset(2026, 2, 2, 10, 0, 0, TimeSpan.Zero), OrderStatuses.Paid, PaymentStatuses.Pending, "Buyer Two", "buyer2@test.com");
            var otherSeller = CreateOrder("ORD-3", "seller-2", 70, 7, 7, new DateTimeOffset(2026, 2, 3, 10, 0, 0, TimeSpan.Zero), OrderStatuses.Paid, PaymentStatuses.Paid, "Buyer Three", "buyer3@test.com");
            appContext.Orders.AddRange(paid, pending, otherSeller);
            await appContext.SaveChangesAsync();

            var service = new AdminReportingService(appContext, productContext, new AdminReportOptions(), NullLogger<AdminReportingService>.Instance);

            var filters = new AdminOrderReportFilterOptions
            {
                SellerId = "seller-1",
                Statuses = new List<string> { OrderStatuses.Paid },
                PaymentStatuses = new List<string> { PaymentStatuses.Paid },
                FromDate = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                ToDate = new DateTimeOffset(2026, 2, 10, 23, 59, 59, TimeSpan.Zero)
            };

            var result = await service.GetOrderReportAsync(filters, pageNumber: 1, pageSize: 20);

            Assert.Equal(1, result.TotalCount);
            var row = Assert.Single(result.Rows);
            Assert.Equal("ORD-1", row.OrderNumber);
            Assert.Equal("seller-1", row.SellerId);
            Assert.Equal(110, row.OrderValue);
            Assert.Equal(10, row.Commission);
            Assert.Equal(100, row.Payout);
        }

        [Fact]
        public async Task ExportOrdersAsync_EnforcesExportLimit()
        {
            await using var appContext = CreateApplicationContext();
            await using var productContext = CreateProductContext();

            var createdOn = new DateTimeOffset(2026, 2, 5, 12, 0, 0, TimeSpan.Zero);
            appContext.Orders.Add(CreateOrder("ORD-10", "seller-1", 30, 5, 3, createdOn, OrderStatuses.Paid, PaymentStatuses.Paid, "Buyer A", "a@test.com"));
            appContext.Orders.Add(CreateOrder("ORD-11", "seller-1", 40, 5, 4, createdOn, OrderStatuses.Paid, PaymentStatuses.Paid, "Buyer B", "b@test.com"));
            appContext.Orders.Add(CreateOrder("ORD-12", "seller-1", 50, 5, 5, createdOn, OrderStatuses.Paid, PaymentStatuses.Paid, "Buyer C", "c@test.com"));
            await appContext.SaveChangesAsync();

            var options = new AdminReportOptions { ExportRowLimit = 2 };
            var service = new AdminReportingService(appContext, productContext, options, NullLogger<AdminReportingService>.Instance);

            var export = await service.ExportOrdersAsync(new AdminOrderReportFilterOptions
            {
                SellerId = "seller-1"
            });

            Assert.NotNull(export);
            Assert.True(export!.Truncated);
            Assert.Equal(2, export.RowCount);
            Assert.Equal(3, export.TotalMatching);
            var csv = System.Text.Encoding.UTF8.GetString(export.Content);
            Assert.Contains("OrderNumber,SubOrderNumber,CreatedOn,Buyer,BuyerEmail,SellerId,SellerName,Status,PaymentStatus,OrderValue,Commission,PayoutAmount", csv);
        }

        [Fact]
        public async Task GetUserAnalyticsAsync_ComputesAggregatesAndSeries()
        {
            await using var appContext = CreateApplicationContext();
            await using var productContext = CreateProductContext();

            var from = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
            var to = new DateTimeOffset(2026, 2, 3, 23, 59, 59, TimeSpan.Zero);

            appContext.Users.Add(new ApplicationUser
            {
                Id = "buyer-1",
                AccountType = AccountTypes.Buyer,
                TermsAcceptedOn = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero),
                FullName = "Buyer One",
                Email = "buyer@test.com",
                UserName = "buyer@test.com",
                NormalizedUserName = "BUYER@TEST.COM",
                NormalizedEmail = "BUYER@TEST.COM",
                Address = "123 Street",
                Country = "PL",
                SecurityStamp = Guid.NewGuid().ToString()
            });
            appContext.Users.Add(new ApplicationUser
            {
                Id = "seller-1",
                AccountType = AccountTypes.Seller,
                TermsAcceptedOn = new DateTimeOffset(2026, 2, 2, 10, 0, 0, TimeSpan.Zero),
                FullName = "Seller One",
                Email = "seller@test.com",
                UserName = "seller@test.com",
                NormalizedUserName = "SELLER@TEST.COM",
                NormalizedEmail = "SELLER@TEST.COM",
                Address = "456 Street",
                Country = "PL",
                SecurityStamp = Guid.NewGuid().ToString()
            });
            appContext.Users.Add(new ApplicationUser
            {
                Id = "legacy",
                AccountType = AccountTypes.Buyer,
                TermsAcceptedOn = new DateTimeOffset(2025, 12, 1, 10, 0, 0, TimeSpan.Zero),
                FullName = "Legacy User",
                Email = "legacy@test.com",
                UserName = "legacy@test.com",
                NormalizedUserName = "LEGACY@TEST.COM",
                NormalizedEmail = "LEGACY@TEST.COM",
                Address = "789 Street",
                Country = "PL",
                SecurityStamp = Guid.NewGuid().ToString()
            });

            appContext.LoginAudits.Add(new LoginAudit
            {
                UserId = "buyer-1",
                Email = "buyer@test.com",
                EventType = LoginEventTypes.PasswordSuccess,
                IsSuccess = true,
                IsUnusual = false,
                OccurredOn = new DateTimeOffset(2026, 2, 3, 8, 0, 0, TimeSpan.Zero),
                ExpiresOn = new DateTimeOffset(2026, 3, 3, 8, 0, 0, TimeSpan.Zero)
            });
            appContext.LoginAudits.Add(new LoginAudit
            {
                UserId = "seller-1",
                Email = "seller@test.com",
                EventType = LoginEventTypes.PasswordSuccess,
                IsSuccess = true,
                IsUnusual = false,
                OccurredOn = new DateTimeOffset(2026, 2, 2, 7, 0, 0, TimeSpan.Zero),
                ExpiresOn = new DateTimeOffset(2026, 3, 2, 7, 0, 0, TimeSpan.Zero)
            });

            var orderCreated = new DateTimeOffset(2026, 2, 3, 12, 0, 0, TimeSpan.Zero);
            appContext.Orders.Add(CreateOrder("ORD-200", "seller-1", 50, 5, 5, orderCreated, OrderStatuses.Paid, PaymentStatuses.Paid, "Buyer One", "buyer@test.com"));

            await appContext.SaveChangesAsync();

            var service = new AdminReportingService(appContext, productContext, new AdminReportOptions(), NullLogger<AdminReportingService>.Instance);

            var analytics = await service.GetUserAnalyticsAsync(from, to);

            Assert.Equal(1, analytics.Summary.NewBuyers);
            Assert.Equal(1, analytics.Summary.NewSellers);
            Assert.Equal(1, analytics.Summary.OrderingUsers);
            Assert.Equal(2, analytics.Summary.LoginUsers);
            Assert.Equal(2, analytics.Summary.ActiveUsers);
            Assert.True(analytics.Summary.HasData);

            Assert.Equal(3, analytics.Series.Count);
            var dayOne = Assert.Single(analytics.Series, p => p.Date == new DateTime(2026, 2, 1));
            Assert.Equal(1, dayOne.NewBuyers);
            var dayThree = Assert.Single(analytics.Series, p => p.Date == new DateTime(2026, 2, 3));
            Assert.Equal(1, dayThree.OrderingUsers);
            Assert.Equal(1, dayThree.LoginUsers);
        }

        private static ApplicationDbContext CreateApplicationContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        private static ProductDbContext CreateProductContext()
        {
            var options = new DbContextOptionsBuilder<ProductDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ProductDbContext(options);
        }

        private static OrderRecord CreateOrder(
            string orderNumber,
            string sellerId,
            decimal itemsSubtotal,
            decimal shipping,
            decimal commission,
            DateTimeOffset createdOn,
            string orderStatus,
            string paymentStatus,
            string buyerName,
            string buyerEmail)
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
                BuyerName = buyerName,
                BuyerEmail = buyerEmail,
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
