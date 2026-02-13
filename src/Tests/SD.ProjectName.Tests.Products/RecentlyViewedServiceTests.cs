using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Moq;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.WebApp.Services;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SD.ProjectName.Tests.Products
{
    public class RecentlyViewedServiceTests
    {
        [Fact]
        public void RememberProduct_ShouldMoveProductToFront_AndRespectLimit()
        {
            var options = new RecentlyViewedOptions { MaxItems = 3, CookieName = ".Test.Recent", CookieLifespanDays = 5 };
            var service = new RecentlyViewedService(new GetProducts(Mock.Of<IProductRepository>()), options, Mock.Of<ILogger<RecentlyViewedService>>());
            var context = new DefaultHttpContext();
            context.Features.Set<IRequestCookiesFeature>(new RequestCookiesFeature(new TestCookieCollection(new Dictionary<string, string>
            {
                { options.CookieName, "5,6,7" }
            })));

            service.RememberProduct(context, 6);

            var cookieHeader = context.Response.Headers["Set-Cookie"].ToString();
            Assert.Contains($"{options.CookieName}=6%2C5%2C7", cookieHeader);
        }

        [Fact]
        public async Task GetProductsAsync_ShouldReturnOrderedActiveProducts_AndPruneMissingOnes()
        {
            var options = new RecentlyViewedOptions { MaxItems = 5, CookieName = ".Test.Recent" };
            var repository = new Mock<IProductRepository>();
            repository.Setup(r => r.GetByIds(It.IsAny<IEnumerable<int>>(), false))
                .ReturnsAsync((IEnumerable<int> ids, bool _) =>
                {
                    var allowed = new[] { 5, 3 };
                    return ids.Where(allowed.Contains)
                        .Select(id => new ProductModel
                        {
                            Id = id,
                            Title = $"Product {id}",
                            Price = 10,
                            Stock = 1,
                            Category = "Cat",
                            MerchantSku = $"SKU-{id}",
                            WorkflowState = ProductWorkflowStates.Active,
                            SellerId = "seller-1"
                        })
                        .ToList();
            });

            var service = new RecentlyViewedService(new GetProducts(repository.Object), options, Mock.Of<ILogger<RecentlyViewedService>>());
            var context = new DefaultHttpContext();
            context.Features.Set<IRequestCookiesFeature>(new RequestCookiesFeature(new TestCookieCollection(new Dictionary<string, string>
            {
                { options.CookieName, "9,5,3,5" }
            })));

            var result = await service.GetProductsAsync(context);

            Assert.Equal(new[] { 5, 3 }, result.Select(p => p.Id).ToArray());
            var cookieHeader = context.Response.Headers["Set-Cookie"].ToString();
            Assert.Contains($"{options.CookieName}=5%2C3", cookieHeader);
        }

        private sealed class TestCookieCollection : IRequestCookieCollection
        {
            private readonly Dictionary<string, string> _cookies;

            public TestCookieCollection(Dictionary<string, string> cookies)
            {
                _cookies = cookies;
            }

            public string this[string key] => _cookies.TryGetValue(key, out var value) ? value : string.Empty;

            public int Count => _cookies.Count;

            public ICollection<string> Keys => _cookies.Keys;

            public bool ContainsKey(string key) => _cookies.ContainsKey(key);

            public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _cookies.GetEnumerator();

            public bool TryGetValue(string key, out string value)
            {
                var found = _cookies.TryGetValue(key, out var stored);
                value = stored ?? string.Empty;
                return found;
            }

            IEnumerator IEnumerable.GetEnumerator() => _cookies.GetEnumerator();
        }
    }
}
