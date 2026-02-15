using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;
using Xunit;

namespace SD.ProjectName.Tests.Products
{
    public class AdminUserActionServiceTests
    {
        [Fact]
        public async Task RecordUserAccessAsync_ShouldPersistAuditEntry()
        {
            await using var appContext = CreateApplicationContext();
            await using var productContext = CreateProductContext();
            var userManager = new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!).Object;
            var timestamp = new DateTimeOffset(2026, 2, 15, 12, 0, 0, TimeSpan.Zero);
            var service = new AdminUserActionService(appContext, productContext, userManager, new FixedTimeProvider(timestamp), NullLogger<AdminUserActionService>.Instance);

            await service.RecordUserAccessAsync("user-123", "admin-1", "Admin Tester", "Viewed profile", "Inspected account", CancellationToken.None);

            var audit = await appContext.UserAdminAudits.SingleAsync();
            Assert.Equal("user-123", audit.UserId);
            Assert.Equal("admin-1", audit.ActorUserId);
            Assert.Equal("Admin Tester", audit.ActorName);
            Assert.Equal("Viewed profile", audit.Action);
            Assert.Equal("Inspected account", audit.Reason);
            Assert.Equal(timestamp, audit.CreatedOn);
        }

        [Fact]
        public void SellerTenantExtension_ReturnsStoreOwnerWhenPresent()
        {
            var user = new ApplicationUser
            {
                Id = "user-1",
                StoreOwnerId = "owner-1"
            };

            Assert.Equal("owner-1", user.GetSellerTenantId());
        }

        private static ApplicationDbContext CreateApplicationContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }

        private static ProductDbContext CreateProductContext()
        {
            var options = new DbContextOptionsBuilder<ProductDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ProductDbContext(options);
        }

        private sealed class FixedTimeProvider : TimeProvider
        {
            private readonly DateTimeOffset _now;

            public FixedTimeProvider(DateTimeOffset now)
            {
                _now = now;
            }

            public override DateTimeOffset GetUtcNow() => _now;
        }
    }
}
