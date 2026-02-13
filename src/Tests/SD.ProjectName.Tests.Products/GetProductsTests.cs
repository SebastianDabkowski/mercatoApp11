using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;

namespace SD.ProjectName.Tests.Products
{
    public class GetProductsTests
    {
        [Fact]
        public async Task GetList_ShouldReturnListOfProducts_WhenRepositoryHasProducts()
        {
            // Arrange
            var expectedProducts = new List<ProductModel>
            {
                new ProductModel { Id = 1, Title = "Product 1", Price = 10.99m, Description = "Description 1", Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-1" },
                new ProductModel { Id = 2, Title = "Product 2", Price = 20.99m, Description = "Description 2", Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-2" },
                new ProductModel { Id = 3, Title = "Product 3", Price = 30.99m, Description = "Description 3", Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-3" }
            };

            var mockRepository = new Mock<IProductRepository>(MockBehavior.Strict);
            mockRepository.Setup(r => r.GetList(null, false)).ReturnsAsync(expectedProducts);

            var getProducts = new GetProducts(mockRepository.Object);

            // Act
            var result = await getProducts.GetList();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Count);
            Assert.Equal(expectedProducts, result);
            mockRepository.Verify(r => r.GetList(null, false), Times.Once);
        }

        [Fact]
        public async Task GetList_ShouldReturnEmptyList_WhenRepositoryHasNoProducts()
        {
            // Arrange
            var expectedProducts = new List<ProductModel>();

            var mockRepository = new Mock<IProductRepository>(MockBehavior.Strict);
            mockRepository.Setup(r => r.GetList(null, false)).ReturnsAsync(expectedProducts);

            var getProducts = new GetProducts(mockRepository.Object);

            // Act
            var result = await getProducts.GetList();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
            mockRepository.Verify(r => r.GetList(null, false), Times.Once);
        }

        [Fact]
        public async Task GetList_ShouldCallRepositoryGetList_Exactly()
        {
            // Arrange
            var expectedProducts = new List<ProductModel>
            {
                new ProductModel { Id = 1, Title = "Test Product", Price = 15.50m, Description = "Test", Category = "Cat", WorkflowState = ProductWorkflowStates.Active, SellerId = "seller-1" }
            };

            var mockRepository = new Mock<IProductRepository>(MockBehavior.Strict);
            mockRepository.Setup(r => r.GetList(null, false)).ReturnsAsync(expectedProducts);

            var getProducts = new GetProducts(mockRepository.Object);

            // Act
            var result = await getProducts.GetList();

            // Assert
            mockRepository.Verify(r => r.GetList(null, false), Times.Once);
        }

        [Fact]
        public void Constructor_ShouldAcceptProductRepository()
        {
            // Arrange
            var mockRepository = new Mock<IProductRepository>(MockBehavior.Strict);

            // Act
            var getProducts = new GetProducts(mockRepository.Object);

            // Assert
            Assert.NotNull(getProducts);
        }

        [Fact]
        public async Task GetList_ShouldIncludeDraftsForSeller_WhenRequested()
        {
            // Arrange
            var sellerId = "seller-1";
            var expectedProducts = new List<ProductModel>
            {
                new ProductModel { Id = 1, Title = "Draft Product", Price = 15, Stock = 5, Category = "Cat", WorkflowState = ProductWorkflowStates.Draft, SellerId = sellerId }
            };

            var mockRepository = new Mock<IProductRepository>(MockBehavior.Strict);
            mockRepository.Setup(r => r.GetList(sellerId, true)).ReturnsAsync(expectedProducts);

            var getProducts = new GetProducts(mockRepository.Object);

            // Act
            var result = await getProducts.GetList(sellerId, includeDrafts: true);

            // Assert
            Assert.Single(result);
            Assert.Equal(ProductWorkflowStates.Draft, result.First().WorkflowState);
            mockRepository.Verify(r => r.GetList(sellerId, true), Times.Once);
        }
    }
}
