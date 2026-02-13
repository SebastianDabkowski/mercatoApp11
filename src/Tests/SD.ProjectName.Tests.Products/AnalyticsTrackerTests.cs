using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class AnalyticsTrackerTests
    {
        [Fact]
        public async Task TrackAsync_ShouldPersistEventWithIdentifiers()
        {
            var dbContext = CreateDbContext();
            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "buyer-1") }, "Test"));
            var accessor = new HttpContextAccessor { HttpContext = httpContext };
            var trackerOptions = new AnalyticsOptions
            {
                Enabled = true,
                SessionCookieName = ".Test.Analytics",
                MaxEventsPerRequest = 5
            };

            var tracker = new AnalyticsTracker(dbContext, trackerOptions, accessor, TimeProvider.System, NullLogger<AnalyticsTracker>.Instance);

            await tracker.TrackAsync(new AnalyticsEventEntry(
                AnalyticsEventTypes.ProductView,
                ProductId: 42,
                SellerId: "seller-1",
                Quantity: 2,
                Metadata: new Dictionary<string, string?> { ["source"] = "details" }));

            var saved = await dbContext.AnalyticsEvents.SingleAsync();
            Assert.Equal(AnalyticsEventTypes.ProductView, saved.EventType);
            Assert.Equal("buyer-1", saved.UserId);
            Assert.Equal(42, saved.ProductId);
            Assert.Equal("seller-1", saved.SellerId);
            Assert.Equal(2, saved.Quantity);
            Assert.False(string.IsNullOrWhiteSpace(saved.SessionId));
            Assert.NotNull(saved.MetadataJson);
        }

        [Fact]
        public async Task TrackAsync_ShouldSkipWhenDisabled()
        {
            var dbContext = CreateDbContext();
            var tracker = new AnalyticsTracker(
                dbContext,
                new AnalyticsOptions { Enabled = false },
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
                TimeProvider.System,
                NullLogger<AnalyticsTracker>.Instance);

            await tracker.TrackAsync(new AnalyticsEventEntry(AnalyticsEventTypes.Search, Keyword: "shoes"));

            Assert.Empty(dbContext.AnalyticsEvents);
        }

        private static ApplicationDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
