using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Pages.Buyer.Orders;
using SD.ProjectName.WebApp.Services;
using Xunit;

namespace SD.ProjectName.Tests.Products
{
    public class BuyerOrderTrackingTests
    {
        [Fact]
        public void BuildTrackingLink_ShouldUseCarrierTemplate()
        {
            var model = CreateModel();

            var link = model.BuildTrackingLink(" 1Z999 ", "UPS");

            Assert.Equal("https://www.ups.com/track?tracknum=1Z999", link);
        }

        [Fact]
        public void BuildTrackingLink_ShouldReturnOriginalUrl_WhenAbsolute()
        {
            var model = CreateModel();

            var link = model.BuildTrackingLink("https://carrier.example/track/ABC123", "UPS");

            Assert.Equal("https://carrier.example/track/ABC123", link);
        }

        [Fact]
        public void BuildTrackingLink_ShouldReturnNull_ForUnknownCarrier()
        {
            var model = CreateModel();

            var link = model.BuildTrackingLink("TRACK-123", "Local Courier");

            Assert.Null(link);
        }

        private static DetailsModel CreateModel()
        {
            var orderService = new OrderService(null!, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);
            var userManager = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!).Object;
            return new DetailsModel(orderService, userManager);
        }
    }
}
