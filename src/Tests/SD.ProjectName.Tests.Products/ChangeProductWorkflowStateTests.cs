using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;

namespace SD.ProjectName.Tests.Products
{
    public class ChangeProductWorkflowStateTests
    {
        [Fact]
        public async Task SetStateAsync_ShouldBlockActivation_WhenRequiredFieldsMissing()
        {
            var repository = new Mock<IProductRepository>();
            var workflow = new ChangeProductWorkflowState(repository.Object);
            var product = CreateValidProduct();
            product.Description = null;
            product.MainImageUrl = null;

            var result = await workflow.SetStateAsync(product, ProductWorkflowStates.Active);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, e => e.Contains("Description"));
            Assert.Contains(result.Errors, e => e.Contains("image"));
            repository.Verify(r => r.Update(It.IsAny<ProductModel>()), Times.Never);
            Assert.Equal(ProductWorkflowStates.Draft, product.WorkflowState);
        }

        [Fact]
        public async Task SetStateAsync_ShouldActivate_WhenRequirementsMet()
        {
            var repository = new Mock<IProductRepository>();
            repository.Setup(r => r.Update(It.IsAny<ProductModel>())).Returns(Task.CompletedTask);
            var workflow = new ChangeProductWorkflowState(repository.Object);
            var product = CreateValidProduct();

            var result = await workflow.SetStateAsync(product, ProductWorkflowStates.Active);

            Assert.True(result.Succeeded);
            Assert.Equal(ProductWorkflowStates.Active, product.WorkflowState);
            repository.Verify(r => r.Update(product), Times.Once);
        }

        [Fact]
        public async Task SetStateAsync_ShouldFail_WhenMovingActiveBackToDraft()
        {
            var repository = new Mock<IProductRepository>();
            var workflow = new ChangeProductWorkflowState(repository.Object);
            var product = CreateValidProduct(ProductWorkflowStates.Active);

            var result = await workflow.SetStateAsync(product, ProductWorkflowStates.Draft);

            Assert.False(result.Succeeded);
            repository.Verify(r => r.Update(It.IsAny<ProductModel>()), Times.Never);
            Assert.Equal(ProductWorkflowStates.Active, product.WorkflowState);
        }

        [Fact]
        public async Task SetStateAsync_ShouldSuspend_WhenActive()
        {
            var repository = new Mock<IProductRepository>();
            repository.Setup(r => r.Update(It.IsAny<ProductModel>())).Returns(Task.CompletedTask);
            var workflow = new ChangeProductWorkflowState(repository.Object);
            var product = CreateValidProduct(ProductWorkflowStates.Active);

            var result = await workflow.SetStateAsync(product, ProductWorkflowStates.Suspended);

            Assert.True(result.Succeeded);
            Assert.Equal(ProductWorkflowStates.Suspended, product.WorkflowState);
            repository.Verify(r => r.Update(product), Times.Once);
        }

        private static ProductModel CreateValidProduct(string state = ProductWorkflowStates.Draft) => new()
        {
            Title = "Sample",
            Price = 10,
            Stock = 5,
            Category = "Electronics",
            CategoryId = 1,
            Description = "A solid product",
            MainImageUrl = "https://cdn.example.com/img.jpg",
            WorkflowState = state,
            SellerId = "seller-1"
        };
    }
}
