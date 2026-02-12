using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.Tests.Identity
{
    public class SessionTokenServiceTests
    {
        [Fact]
        public async Task Issue_ShouldCreateUsableToken()
        {
            var service = CreateService(out var timeProvider, new SessionTokenOptions { TokenLifetimeMinutes = 30 });

            var session = await service.IssueAsync("user-1", CancellationToken.None);

            Assert.False(string.IsNullOrWhiteSpace(session.Token));
            Assert.True(await service.ValidateAsync("user-1", session.Token, CancellationToken.None));
            Assert.True(session.ExpiresAt > timeProvider.UtcNow);
        }

        [Fact]
        public async Task ExpiredToken_ShouldFailValidation()
        {
            var service = CreateService(out var timeProvider, new SessionTokenOptions { TokenLifetimeMinutes = 1 });
            var session = await service.IssueAsync("user-2", CancellationToken.None);

            timeProvider.Advance(TimeSpan.FromMinutes(2));

            Assert.False(await service.ValidateAsync("user-2", session.Token, CancellationToken.None));
        }

        [Fact]
        public async Task RevokedToken_ShouldBeInvalidated()
        {
            var service = CreateService(out _, new SessionTokenOptions { TokenLifetimeMinutes = 10 });
            var session = await service.IssueAsync("user-3", CancellationToken.None);

            await service.RevokeAsync("user-3", session.Token, CancellationToken.None);

            Assert.False(await service.ValidateAsync("user-3", session.Token, CancellationToken.None));
        }

        private static DistributedSessionTokenService CreateService(out TestTimeProvider timeProvider, SessionTokenOptions options)
        {
            var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            timeProvider = new TestTimeProvider();
            return new DistributedSessionTokenService(
                cache,
                Mock.Of<ILogger<DistributedSessionTokenService>>(),
                Options.Create(options),
                timeProvider);
        }

        private class TestTimeProvider : TimeProvider
        {
            public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;

            public override DateTimeOffset GetUtcNow() => UtcNow;

            public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
        }
    }
}
