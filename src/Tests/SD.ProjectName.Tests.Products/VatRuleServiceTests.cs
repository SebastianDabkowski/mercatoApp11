using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;
using Xunit;

namespace SD.ProjectName.Tests.Products
{
    public class VatRuleServiceTests
    {
        [Fact]
        public async Task Resolve_UsesCountryRuleAndFallback()
        {
            await using var context = CreateContext();
            var service = new VatRuleService(context, new InvoiceOptions { TaxRate = 0.1m });
            var now = DateTimeOffset.UtcNow;

            context.VatRules.Add(new VatRule
            {
                Country = "PL",
                Rate = 0.23m,
                EffectiveFrom = now.AddDays(-3)
            });
            context.VatRules.Add(new VatRule
            {
                Country = "PL",
                Rate = 0.25m,
                Categories = "Books",
                EffectiveFrom = now.AddDays(1)
            });
            await context.SaveChangesAsync();

            var active = service.Resolve("pl", "Home", now);
            Assert.Equal(0.23m, active.Rate);

            var future = service.Resolve("PL", "Books", now.AddDays(2));
            Assert.Equal(0.25m, future.Rate);

            var fallback = service.Resolve("US", null, now);
            Assert.Equal(0.1m, fallback.Rate);
        }

        [Fact]
        public async Task SaveAsync_RejectsOverlappingForCountryAndCategory()
        {
            await using var context = CreateContext();
            var service = new VatRuleService(context, new InvoiceOptions());
            var today = DateTimeOffset.UtcNow.Date;

            var first = await service.SaveAsync(
                new VatRuleInput
                {
                    Country = "PL",
                    Rate = 0.2m,
                    Categories = new[] { "Electronics" },
                    EffectiveFrom = today
                },
                "admin",
                "Admin");

            Assert.True(first.Success);

            var overlap = await service.SaveAsync(
                new VatRuleInput
                {
                    Country = "PL",
                    Rate = 0.22m,
                    Categories = new[] { "electronics" },
                    EffectiveFrom = today.AddDays(-1),
                    EffectiveTo = today.AddDays(2)
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
