using SD.ProjectName.Modules.Products.Domain;
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
    }
}
