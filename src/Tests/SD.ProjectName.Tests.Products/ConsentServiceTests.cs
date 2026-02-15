using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class ConsentServiceTests
    {
        [Fact]
        public async Task RecordUserConsents_ShouldPersistVersionedDecision()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var dbContext = new ApplicationDbContext(options);
            var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
            var service = new ConsentService(dbContext, clock);

            var definitions = await service.GetActiveConsentsAsync();
            var result = await service.RecordUserConsentsAsync(
                "user-1",
                definitions.ToDictionary(d => d.ConsentType, _ => true));

            Assert.True(result.Success);
            var snapshots = await service.GetUserConsentsAsync("user-1");
            Assert.Equal(definitions.Count, snapshots.Count);
            var newsletter = snapshots.First(s => s.ConsentType == ConsentTypes.Newsletter);
            Assert.True(newsletter.Granted);
            Assert.False(string.IsNullOrWhiteSpace(newsletter.Version.VersionTag));
        }

        [Fact]
        public async Task HasActiveConsentAsync_ShouldRequireLatestVersion()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var dbContext = new ApplicationDbContext(options);
            var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
            var service = new ConsentService(dbContext, clock);
            var definitions = await service.GetActiveConsentsAsync();
            await service.RecordUserConsentsAsync("user-2", definitions.ToDictionary(d => d.ConsentType, _ => true));

            Assert.True(await service.HasActiveConsentAsync("user-2", ConsentTypes.Newsletter));

            var now = clock.GetUtcNow();
            var definition = await dbContext.ConsentDefinitions.Include(d => d.Versions)
                .FirstAsync(d => d.ConsentType == ConsentTypes.Newsletter);
            definition.Versions.Add(new ConsentVersion
            {
                VersionTag = "v-next",
                Content = "Updated content",
                EffectiveFrom = now.AddMinutes(1),
                CreatedOn = now.AddMinutes(1)
            });
            await dbContext.SaveChangesAsync();

            clock.Advance(TimeSpan.FromMinutes(2));
            Assert.False(await service.HasActiveConsentAsync("user-2", ConsentTypes.Newsletter));

            await service.RecordUserConsentsAsync("user-2", new Dictionary<string, bool> { [ConsentTypes.Newsletter] = true });
            Assert.True(await service.HasActiveConsentAsync("user-2", ConsentTypes.Newsletter));
        }

        [Fact]
        public async Task MarketingEmailService_ShouldSendOnlyWithConsent()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var dbContext = new ApplicationDbContext(options);
            var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
            var consentService = new ConsentService(dbContext, clock);
            await consentService.GetActiveConsentsAsync();

            var emailSender = new Mock<IEmailSender>();
            var marketing = new MarketingEmailService(consentService, emailSender.Object);

            var sent = await marketing.SendAsync("user-3", "user3@example.com", "Subject", "<p>Body</p>");
            Assert.False(sent);
            emailSender.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);

            await consentService.RecordUserConsentsAsync("user-3", new Dictionary<string, bool>
            {
                [ConsentTypes.Newsletter] = true
            });

            sent = await marketing.SendAsync("user-3", "user3@example.com", "Subject", "<p>Body</p>");
            Assert.True(sent);
            emailSender.Verify(e => e.SendEmailAsync("user3@example.com", "Subject", "<p>Body</p>"), Times.Once);
        }
    }

    internal class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public TestTimeProvider(DateTimeOffset start)
        {
            _utcNow = start;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new NotSupportedException();
    }
}
