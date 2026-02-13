using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class NotificationServiceTests
    {
        [Fact]
        public async Task GetFeedAsync_ShouldReturnUnreadOnly_WhenFiltered()
        {
            var service = new NotificationService(TimeProvider.System);

            var result = await service.GetFeedAsync("user-1", "Buyer", NotificationFilter.Unread, 1, 10);

            Assert.Equal(2, result.UnreadCount);
            Assert.All(result.Items.Items, n => Assert.False(n.IsRead));
            Assert.Equal(2, result.Items.TotalCount);
        }

        [Fact]
        public async Task MarkAsReadAsync_ShouldLowerUnreadCount()
        {
            var service = new NotificationService(TimeProvider.System);
            var firstPage = await service.GetFeedAsync("user-2", "Buyer", NotificationFilter.Unread, 1, 10);
            var notificationId = firstPage.Items.Items.First().Id;

            var marked = await service.MarkAsReadAsync("user-2", notificationId);
            var after = await service.GetUnreadCountAsync("user-2");

            Assert.True(marked);
            Assert.Equal(1, after);
        }

        [Fact]
        public async Task GetFeedAsync_ShouldPaginateResults()
        {
            var service = new NotificationService(TimeProvider.System);

            var page1 = await service.GetFeedAsync("user-3", "Buyer", NotificationFilter.All, 1, 2);
            var page2 = await service.GetFeedAsync("user-3", "Buyer", NotificationFilter.All, 2, 2);

            Assert.Equal(5, page1.Items.TotalCount);
            Assert.Equal(2, page1.Items.Items.Count);
            Assert.Equal(2, page2.Items.Items.Count);
            Assert.NotEqual(page1.Items.Items[0].Id, page2.Items.Items[0].Id);
        }
    }
}
