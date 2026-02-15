using System;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;
using Xunit;

namespace SD.ProjectName.Tests.Products
{
    public class LegalDocumentServiceTests
    {
        [Fact]
        public async Task GetActiveAndUpcoming_ShouldRespectEffectiveDates()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new ApplicationDbContext(options);
            db.LegalDocumentVersions.AddRange(
                new LegalDocumentVersion
                {
                    DocumentType = LegalDocumentTypes.TermsOfService,
                    VersionTag = "v1",
                    Content = "Old",
                    EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-10),
                    CreatedOn = DateTimeOffset.UtcNow.AddDays(-11)
                },
                new LegalDocumentVersion
                {
                    DocumentType = LegalDocumentTypes.TermsOfService,
                    VersionTag = "v2",
                    Content = "Current",
                    EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
                    CreatedOn = DateTimeOffset.UtcNow.AddDays(-2)
                },
                new LegalDocumentVersion
                {
                    DocumentType = LegalDocumentTypes.TermsOfService,
                    VersionTag = "v3",
                    Content = "Upcoming",
                    EffectiveFrom = DateTimeOffset.UtcNow.AddDays(5),
                    CreatedOn = DateTimeOffset.UtcNow
                });
            await db.SaveChangesAsync();

            var service = new LegalDocumentService(db);
            var now = DateTimeOffset.UtcNow;

            var active = await service.GetActiveVersionAsync(LegalDocumentTypes.TermsOfService, now);
            var upcoming = await service.GetUpcomingVersionAsync(LegalDocumentTypes.TermsOfService, now);

            Assert.NotNull(active);
            Assert.Equal("v2", active!.VersionTag);
            Assert.NotNull(upcoming);
            Assert.Equal("v3", upcoming!.VersionTag);
        }

        [Fact]
        public async Task SaveAsync_ShouldAutoGenerateVersionTag()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            await using var db = new ApplicationDbContext(options);
            var service = new LegalDocumentService(db);

            var result = await service.SaveAsync(
                new LegalDocumentInput
                {
                    DocumentType = LegalDocumentTypes.PrivacyPolicy,
                    Content = "<p>Policy</p>",
                    EffectiveFrom = DateTimeOffset.UtcNow
                },
                "admin",
                "Admin");

            Assert.True(result.Success);
            Assert.NotNull(result.Version);
            Assert.False(string.IsNullOrWhiteSpace(result.Version!.VersionTag));
        }
    }
}
