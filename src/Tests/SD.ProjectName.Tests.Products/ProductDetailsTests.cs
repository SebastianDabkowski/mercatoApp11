using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.WebApp.Pages.Products;

namespace SD.ProjectName.Tests.Products
{
    public class ProductDetailsTests
    {
        [Fact]
        public async Task OnGet_ShouldReturnUnavailable_WhenProductMissingOrArchived()
        {
            var repo = new Mock<IProductRepository>();
            repo.Setup(r => r.GetById(5, false)).ReturnsAsync((ProductModel?)null);
            var model = CreateModel(repo.Object);

            var result = await model.OnGetAsync(5);

            Assert.IsType<PageResult>(result);
            Assert.Null(model.Product);
            Assert.Equal(StatusCodes.Status404NotFound, model.Response.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace(model.StatusMessage));
        }

        [Fact]
        public async Task OnGet_ShouldExposeProduct_WhenActive()
        {
            var repo = new Mock<IProductRepository>();
            repo.Setup(r => r.GetById(7, false)).ReturnsAsync(new ProductModel
            {
                Id = 7,
                Title = "Active Product",
                MerchantSku = "SKU-7",
                Price = 10,
                Stock = 3,
                Category = "Cat",
                WorkflowState = ProductWorkflowStates.Active,
                SellerId = "seller-1"
            });

            var model = CreateModel(repo.Object);

            var result = await model.OnGetAsync(7);

            Assert.IsType<PageResult>(result);
            Assert.NotNull(model.Product);
            Assert.Equal("Active Product", model.Product!.Title);
            Assert.NotEqual(StatusCodes.Status404NotFound, model.Response.StatusCode);
        }

        private static DetailsModel CreateModel(IProductRepository repository)
        {
            var getProducts = new GetProducts(repository);
            var logger = Mock.Of<ILogger<DetailsModel>>();
            var httpContext = new DefaultHttpContext();
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var pageContext = new PageContext(actionContext);

            var model = new DetailsModel(getProducts, logger)
            {
                PageContext = pageContext
            };

            return model;
        }
    }
}
