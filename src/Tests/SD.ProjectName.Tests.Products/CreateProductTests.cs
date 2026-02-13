using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;

namespace SD.ProjectName.Tests.Products
{
    public class CreateProductTests
    {
        [Fact]
        public async Task CreateAsync_ShouldSetDraftWhenWorkflowNotProvided()
        {
            // Arrange
            var product = new ProductModel
            {
                Title = "New Product",
                MerchantSku = "SKU-NEW",
                Price = 20,
                Stock = 10,
                Category = "Default",
                SellerId = "seller-1",
                WorkflowState = string.Empty
            };

            var repository = new Mock<IProductRepository>(MockBehavior.Strict);
            repository.Setup(r => r.Add(It.Is<ProductModel>(p => p.WorkflowState == ProductWorkflowStates.Draft)))
                      .Returns(Task.CompletedTask);

            var createProduct = new CreateProduct(repository.Object);

            // Act
            await createProduct.CreateAsync(product);

            // Assert
            repository.Verify(r => r.Add(It.IsAny<ProductModel>()), Times.Once);
            Assert.Equal(ProductWorkflowStates.Draft, product.WorkflowState);
        }

        [Fact]
        public async Task CreateAsync_ShouldPreserveWorkflowState_WhenProvided()
        {
            // Arrange
            var product = new ProductModel
            {
                Title = "Active Product",
                MerchantSku = "SKU-ACTIVE",
                Price = 30,
                Stock = 3,
                Category = "Default",
                SellerId = "seller-1",
                WorkflowState = ProductWorkflowStates.Active
            };

            var repository = new Mock<IProductRepository>(MockBehavior.Strict);
            repository.Setup(r => r.Add(It.Is<ProductModel>(p => p.WorkflowState == ProductWorkflowStates.Active)))
                      .Returns(Task.CompletedTask);

            var createProduct = new CreateProduct(repository.Object);

            // Act
            await createProduct.CreateAsync(product);

            // Assert
            repository.Verify(r => r.Add(It.Is<ProductModel>(p => p.WorkflowState == ProductWorkflowStates.Active)), Times.Once);
        }
    }
}
