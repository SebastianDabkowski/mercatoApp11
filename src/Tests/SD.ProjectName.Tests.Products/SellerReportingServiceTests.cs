using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;
using Xunit;

namespace SD.ProjectName.Tests.Products
{
    public class SellerReportingServiceTests
    {
        private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        [Fact]
        public async Task GetSalesAsync_ShouldReturnSellerOnlyData_ByDay()
        {
            await using var appContext = CreateApplicationContext();
            await using var productContext = CreateProductContext();

            var orderOne = CreateOrder(
                "ORD-1",
                "seller-1",
                new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero),
                40,
                10,
                0,
                new List<OrderItemDetail>
                {
                    new(101, "Item A", "Default", 2, 20, 40, "seller-1", "Seller One", OrderStatuses.Paid, "Home/Kitchen")
                });
            var orderTwo = CreateOrder(
                "ORD-2",
                "seller-1",
                new DateTimeOffset(2026, 2, 2, 10, 0, 0, TimeSpan.Zero),
                20,
                5,
                0,
                new List<OrderItemDetail>
                {
                    new(102, "Item B", "Default", 1, 20, 20, "seller-1", "Seller One", OrderStatuses.Paid, "Home/Kitchen")
                });
            var otherSeller = CreateOrder(
                "ORD-3",
                "seller-2",
                new DateTimeOffset(2026, 2, 2, 12, 0, 0, TimeSpan.Zero),
                30,
                5,
                0,
                new List<OrderItemDetail>
                {
                    new(201, "Other", "Default", 1, 30, 30, "seller-2", "Seller Two", OrderStatuses.Paid, "Electronics")
                });

            appContext.Orders.AddRange(orderOne, orderTwo, otherSeller);
            await appContext.SaveChangesAsync();

            var service = new SellerReportingService(appContext, productContext, NullLogger<SellerReportingService>.Instance);
            var result = await service.GetSalesAsync(
                "seller-1",
                new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 2, 2, 23, 59, 59, TimeSpan.Zero),
                SellerSalesGranularities.Day,
                null,
                null);

            Assert.Equal(2, result.TotalOrders);
            Assert.Equal(75, result.TotalGmv);
            Assert.Equal(2, result.Series.Count);
            Assert.Contains(result.Series, s => s.Label.Contains("Feb 01", StringComparison.OrdinalIgnoreCase) && s.Gmv == 50 && s.Orders == 1);
            Assert.Contains(result.Series, s => s.Label.Contains("Feb 02", StringComparison.OrdinalIgnoreCase) && s.Gmv == 25 && s.Orders == 1);
        }

        [Fact]
        public async Task GetSalesAsync_ShouldFilterByProductAndCategory()
        {
            await using var appContext = CreateApplicationContext();
            await using var productContext = CreateProductContext();

            var kitchenCategory = new CategoryModel { Id = 1, Name = "Kitchen", FullPath = "Home/Kitchen" };
            var electronicsCategory = new CategoryModel { Id = 2, Name = "Electronics", FullPath = "Electronics" };
            productContext.Categories.AddRange(kitchenCategory, electronicsCategory);
            productContext.Products.Add(new ProductModel { Id = 1, Title = "Pan", Price = 20, SellerId = "seller-1", Category = kitchenCategory.FullPath, CategoryId = kitchenCategory.Id, WorkflowState = ProductWorkflowStates.Active });
            productContext.Products.Add(new ProductModel { Id = 2, Title = "Headphones", Price = 10, SellerId = "seller-1", Category = electronicsCategory.FullPath, CategoryId = electronicsCategory.Id, WorkflowState = ProductWorkflowStates.Active });
            await productContext.SaveChangesAsync();

            var mixedOrder = CreateOrder(
                "ORD-10",
                "seller-1",
                new DateTimeOffset(2026, 2, 5, 9, 0, 0, TimeSpan.Zero),
                30,
                6,
                0,
                new List<OrderItemDetail>
                {
                    new(1, "Pan", "Default", 1, 20, 20, "seller-1", "Seller One", OrderStatuses.Paid, kitchenCategory.FullPath),
                    new(2, "Headphones", "Default", 1, 10, 10, "seller-1", "Seller One", OrderStatuses.Paid, electronicsCategory.FullPath)
                });
            appContext.Orders.Add(mixedOrder);
            await appContext.SaveChangesAsync();

            var service = new SellerReportingService(appContext, productContext, NullLogger<SellerReportingService>.Instance);

            var productResult = await service.GetSalesAsync(
                "seller-1",
                new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 2, 5, 23, 59, 59, TimeSpan.Zero),
                SellerSalesGranularities.Day,
                productId: 1,
                categoryId: null);

            Assert.Equal(1, productResult.TotalOrders);
            Assert.Equal(24, Math.Round(productResult.TotalGmv, 2));

            var categoryResult = await service.GetSalesAsync(
                "seller-1",
                new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 2, 5, 23, 59, 59, TimeSpan.Zero),
                SellerSalesGranularities.Day,
                productId: null,
                categoryId: electronicsCategory.Id);

            Assert.Equal(1, categoryResult.TotalOrders);
            Assert.Equal(12, Math.Round(categoryResult.TotalGmv, 2));
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
            DateTimeOffset createdOn,
            decimal itemsSubtotal,
            decimal shipping,
            decimal discount,
            List<OrderItemDetail> items)
        {
            var grandTotal = Math.Max(0, itemsSubtotal + shipping - discount);
            var subOrder = new OrderSubOrder(
                $"{orderNumber}-01",
                sellerId,
                sellerId,
                itemsSubtotal,
                shipping,
                discount,
                grandTotal,
                items.Sum(i => i.Quantity),
                items,
                new OrderShippingDetail(sellerId, sellerId, "standard", "Standard", shipping, "Standard shipping"),
                OrderStatuses.Paid,
                null,
                null,
                0);
            var details = new OrderDetailsPayload(
                items,
                new List<OrderShippingDetail> { subOrder.ShippingDetail },
                subOrder.TotalQuantity,
                discount,
                null,
                new List<OrderSubOrder> { subOrder },
                new List<EscrowAllocation>(),
                PaymentStatuses.Paid);

            return new OrderRecord
            {
                OrderNumber = orderNumber,
                Status = OrderStatuses.Paid,
                ItemsSubtotal = itemsSubtotal,
                ShippingTotal = shipping,
                GrandTotal = grandTotal,
                TotalQuantity = subOrder.TotalQuantity,
                CreatedOn = createdOn,
                DetailsJson = JsonSerializer.Serialize(details, SerializerOptions),
                PaymentMethodId = "card",
                PaymentMethodLabel = "Card",
                BuyerEmail = "buyer@example.com",
                BuyerName = "Buyer",
                DeliveryAddressJson = "{}"
            };
        }
    }
}
