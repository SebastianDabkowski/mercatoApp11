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
                new ProductModel { Id = 1, Name = "Product 1", Price = 10.99m, Description = "Description 1" },
                new ProductModel { Id = 2, Name = "Product 2", Price = 20.99m, Description = "Description 2" },
                new ProductModel { Id = 3, Name = "Product 3", Price = 30.99m, Description = "Description 3" }
            };

            var mockRepository = new Mock<IProductRepository>(MockBehavior.Strict);
            mockRepository.Setup(r => r.GetList()).ReturnsAsync(expectedProducts);

            var getProducts = new GetProducts(mockRepository.Object);

            // Act
            var result = await getProducts.GetList();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Count);
            Assert.Equal(expectedProducts, result);
            mockRepository.Verify(r => r.GetList(), Times.Once);
        }

        [Fact]
        public async Task GetList_ShouldReturnEmptyList_WhenRepositoryHasNoProducts()
        {
            // Arrange
            var expectedProducts = new List<ProductModel>();

            var mockRepository = new Mock<IProductRepository>(MockBehavior.Strict);
            mockRepository.Setup(r => r.GetList()).ReturnsAsync(expectedProducts);

            var getProducts = new GetProducts(mockRepository.Object);

            // Act
            var result = await getProducts.GetList();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
            mockRepository.Verify(r => r.GetList(), Times.Once);
        }

        [Fact]
        public async Task GetList_ShouldCallRepositoryGetList_Exactly()
        {
            // Arrange
            var expectedProducts = new List<ProductModel>
            {
                new ProductModel { Id = 1, Name = "Test Product", Price = 15.50m, Description = "Test" }
            };

            var mockRepository = new Mock<IProductRepository>(MockBehavior.Strict);
            mockRepository.Setup(r => r.GetList()).ReturnsAsync(expectedProducts);

            var getProducts = new GetProducts(mockRepository.Object);

            // Act
            var result = await getProducts.GetList();

            // Assert
            mockRepository.Verify(r => r.GetList(), Times.Once);
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
    }
}
