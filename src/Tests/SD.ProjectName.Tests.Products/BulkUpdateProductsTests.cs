using System.Linq;
using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;

namespace SD.ProjectName.Tests.Products
{
    public class BulkUpdateProductsTests
    {
        [Fact]
        public async Task Preview_ShouldFlagNegativePriceOutcome()
        {
            var product = new ProductModel
            {
                Id = 1,
                Title = "Test",
                Price = 10,
                Stock = 5,
                Category = "Cat",
                WorkflowState = ProductWorkflowStates.Active,
                SellerId = "seller-1"
            };

            var repository = new Mock<IProductRepository>();
            repository.Setup(r => r.GetByIds(It.IsAny<IEnumerable<int>>(), true))
                .ReturnsAsync(new List<ProductModel> { product });

            var service = new BulkUpdateProducts(repository.Object);
            var command = new BulkUpdateCommand
            {
                PriceOperation = BulkPriceOperation.DecreasePercent,
                PriceValue = 150
            };

            var result = await service.PreviewAsync("seller-1", new[] { product.Id }, command);

            Assert.Single(result.Items);
            Assert.Equal("Price cannot be zero or negative.", result.Items[0].Error);
            Assert.Equal(0, result.AppliedCount);
        }

        [Fact]
        public async Task Apply_ShouldUpdateOwnedProducts()
        {
            ProductModel? saved = null;
            var product = new ProductModel
            {
                Id = 2,
                Title = "Owned",
                Price = 20,
                Stock = 4,
                Category = "Cat",
                WorkflowState = ProductWorkflowStates.Active,
                SellerId = "seller-1"
            };

            var repository = new Mock<IProductRepository>();
            repository.Setup(r => r.GetByIds(It.IsAny<IEnumerable<int>>(), true))
                .ReturnsAsync(new List<ProductModel> { product });
            repository.Setup(r => r.Update(It.IsAny<ProductModel>()))
                .Callback<ProductModel>(p => saved = p)
                .Returns(Task.CompletedTask);

            var service = new BulkUpdateProducts(repository.Object);
            var command = new BulkUpdateCommand
            {
                PriceOperation = BulkPriceOperation.IncreasePercent,
                PriceValue = 10,
                StockOperation = BulkStockOperation.Decrease,
                StockValue = 1
            };

            var result = await service.ApplyAsync("seller-1", new[] { product.Id }, command);

            Assert.Equal(1, result.AppliedCount);
            Assert.NotNull(saved);
            Assert.Equal(22, saved!.Price);
            Assert.Equal(3, saved.Stock);
            Assert.True(result.Items.First().Applied);
            Assert.True(string.IsNullOrWhiteSpace(result.Items.First().Error));
        }
    }
}
