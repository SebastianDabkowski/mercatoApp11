using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;
using Xunit;

namespace SD.ProjectName.Tests.Products
{
    public class CommissionRuleServiceTests
    {
        [Fact]
        public async Task Resolve_UsesActiveRuleAndFixedFee()
        {
            await using var context = CreateContext();
            var options = new CartOptions { PlatformCommissionRate = 0.05m, PlatformFixedFee = 0.5m };
            var service = new CommissionRuleService(context, options);
            var now = DateTimeOffset.UtcNow;

            context.CommissionRules.Add(new CommissionRule
            {
                Name = "Default window",
                Rate = 0.12m,
                FixedFee = 1.2m,
                EffectiveFrom = now.AddDays(-2)
            });
            context.CommissionRules.Add(new CommissionRule
            {
                Name = "Category future",
                Rate = 0.2m,
                FixedFee = 2.5m,
                Category = "Electronics",
                EffectiveFrom = now.AddDays(1)
            });
            await context.SaveChangesAsync();

            var active = service.Resolve("seller-1", "Home", null, now);
            Assert.Equal(0.12m, active.Rate);
            Assert.Equal(1.2m, active.FixedFee);

            var future = service.Resolve("seller-1", "Electronics", null, now.AddDays(2));
            Assert.Equal(0.2m, future.Rate);
            Assert.Equal(2.5m, future.FixedFee);
        }

        [Fact]
        public async Task SaveAsync_RejectsOverlappingRules()
        {
            await using var context = CreateContext();
            var service = new CommissionRuleService(context, new CartOptions());
            var today = DateTimeOffset.UtcNow.Date;

            var first = await service.SaveAsync(
                new CommissionRuleInput
                {
                    Name = "Books window",
                    Rate = 0.1m,
                    FixedFee = 0,
                    Category = "Books",
                    EffectiveFrom = today
                },
                "admin",
                "Admin");
            Assert.True(first.Success);

            var overlap = await service.SaveAsync(
                new CommissionRuleInput
                {
                    Name = "Overlap",
                    Rate = 0.15m,
                    FixedFee = 0,
                    Category = "Books",
                    EffectiveFrom = today.AddDays(-1),
                    EffectiveTo = today.AddDays(5)
                },
                "admin",
                "Admin");

            Assert.False(overlap.Success);
            Assert.Contains(overlap.Errors, e => e.Contains("Conflicts", StringComparison.OrdinalIgnoreCase));
        }

        private static ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
