using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class ShippingOptionsServiceTests
    {
        [Fact]
        public void BuildQuote_ShouldUseSelectionsAndUpdateTotals()
        {
            var checkoutOptions = new CheckoutOptions
            {
                DefaultShippingMethods = new List<string> { "Standard", "Express" }
            };
            var service = new ShippingOptionsService(checkoutOptions);
            var product = new ProductModel
            {
                Id = 1,
                SellerId = "seller-1",
                Title = "Sample",
                MerchantSku = "SKU-1",
                Price = 20,
                Stock = 5,
                Category = "General",
                WorkflowState = ProductWorkflowStates.Active,
                ShippingMethods = "Standard, Express"
            };

            var items = new List<CartDisplayItem>
            {
                new(product, 2, string.Empty, product.Price, product.Price * 2, true, product.Stock, new Dictionary<string, string>())
            };

            var sellerGroups = new List<CartSellerGroup>
            {
                new(product.SellerId, "Seller One", 40, 5, 45, items)
            };
            var settlements = new List<CartSellerSettlement>
            {
                new(product.SellerId, 40, 5, 4, 41)
            };
            var summary = new CartSummary(sellerGroups, 40, 5, 45, 2, new CartSettlementSummary(settlements, 4, 41));
            var address = new DeliveryAddress("Jane Doe", "123 Main St", null, "Springfield", "IL", "12345", "US", null);

            var quote = service.BuildQuote(summary, address, new Dictionary<string, string> { ["seller-1"] = "US" }, new Dictionary<string, string> { ["seller-1"] = "express" });

            var sellerOption = Assert.Single(quote.SellerOptions);
            Assert.Equal("seller-1", sellerOption.SellerId);
            Assert.Contains(sellerOption.Options, o => o.Id == "express");

            Assert.Equal("express", quote.SelectedMethods["seller-1"]);
            Assert.True(quote.Summary.ShippingTotal >= summary.ShippingTotal);
            Assert.Equal(quote.Summary.ItemsSubtotal + quote.Summary.ShippingTotal, quote.Summary.GrandTotal);

            var settlement = Assert.Single(quote.Summary.Settlement.Sellers);
            Assert.Equal(quote.Summary.SellerGroups.Single().Shipping, settlement.Shipping);
        }

        [Fact]
        public void BuildQuote_ShouldPreferConfiguredSellerMethods()
        {
            var checkoutOptions = new CheckoutOptions
            {
                DefaultShippingMethods = new List<string> { "Standard", "Express" }
            };
            var service = new ShippingOptionsService(checkoutOptions);
            var product = new ProductModel
            {
                Id = 1,
                SellerId = "seller-1",
                Title = "Sample",
                MerchantSku = "SKU-1",
                Price = 10,
                Stock = 3,
                Category = "General",
                WorkflowState = ProductWorkflowStates.Active,
                ShippingMethods = "Standard"
            };

            var items = new List<CartDisplayItem>
            {
                new(product, 1, string.Empty, product.Price, product.Price, true, product.Stock, new Dictionary<string, string>())
            };

            var sellerGroups = new List<CartSellerGroup>
            {
                new(product.SellerId, "Seller One", 10, 5, 15, items)
            };
            var settlements = new List<CartSellerSettlement>
            {
                new(product.SellerId, 10, 5, 1, 9)
            };
            var summary = new CartSummary(sellerGroups, 10, 5, 15, 1, new CartSettlementSummary(settlements, 1, 9));
            var address = new DeliveryAddress("Jane Doe", "123 Main St", null, "Springfield", "IL", "60601", "US", null);

            var sellerMethods = new Dictionary<string, List<SellerShippingMethod>>(StringComparer.OrdinalIgnoreCase)
            {
                ["seller-1"] = new List<SellerShippingMethod>
                {
                    new SellerShippingMethod
                    {
                        Name = "Parcel locker",
                        Description = "Locker delivery",
                        BaseCost = 12,
                        DeliveryEstimate = "2-3 business days",
                        IsActive = true,
                        IsDeleted = false,
                        StoreOwnerId = "seller-1",
                        CreatedOn = DateTimeOffset.UtcNow,
                        UpdatedOn = DateTimeOffset.UtcNow
                    }
                }
            };

            var quote = service.BuildQuote(summary, address, new Dictionary<string, string> { ["seller-1"] = "US" }, null, sellerMethods);

            var sellerOption = Assert.Single(quote.SellerOptions);
            var option = Assert.Single(sellerOption.Options);
            Assert.Equal("Parcel locker", option.Label);
            Assert.Equal("Locker delivery", option.Description);
            Assert.Equal(12, option.Cost);
            Assert.Equal("2-3 business days", option.DeliveryEstimate);
            Assert.Equal(12, quote.Summary.ShippingTotal);
            Assert.Equal(22, quote.Summary.GrandTotal);
        }
    }
}
