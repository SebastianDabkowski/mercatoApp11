using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace SD.ProjectName.WebApp.Identity
{
    public record SessionToken(string Token, DateTimeOffset ExpiresAt);

    public interface ISessionTokenService
    {
        Task<SessionToken> IssueAsync(string userId, CancellationToken cancellationToken);

        Task<bool> ValidateAsync(string userId, string? token, CancellationToken cancellationToken);

        Task RevokeAsync(string userId, string? token, CancellationToken cancellationToken);
    }

    public class DistributedSessionTokenService : ISessionTokenService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly IDistributedCache _cache;
        private readonly ILogger<DistributedSessionTokenService> _logger;
        private readonly SessionTokenOptions _options;
        private readonly TimeProvider _timeProvider;

        public DistributedSessionTokenService(
            IDistributedCache cache,
            ILogger<DistributedSessionTokenService> logger,
            IOptions<SessionTokenOptions> options,
            TimeProvider timeProvider)
        {
            _cache = cache;
            _logger = logger;
            _options = options.Value;
            _timeProvider = timeProvider;
        }

        public async Task<SessionToken> IssueAsync(string userId, CancellationToken cancellationToken)
        {
            var tokenId = Guid.NewGuid().ToString("N");
            var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var token = $"{tokenId}.{secret}";
            var expiresAt = _timeProvider.GetUtcNow().Add(_options.Lifetime);
            var payload = new SessionTokenPayload
            {
                UserId = userId,
                TokenHash = HashSecret(secret),
                ExpiresAt = expiresAt
            };

            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _options.Lifetime
            };

            await _cache.SetStringAsync(BuildCacheKey(tokenId), JsonSerializer.Serialize(payload, SerializerOptions), cacheOptions, cancellationToken);

            return new SessionToken(token, expiresAt);
        }

        public async Task<bool> ValidateAsync(string userId, string? token, CancellationToken cancellationToken)
        {
            if (!TryParseToken(token, out var tokenId, out var secret))
            {
                return false;
            }

            var payload = await GetPayloadAsync(tokenId, cancellationToken);
            if (payload == null)
            {
                return false;
            }

            if (!string.Equals(payload.UserId, userId, StringComparison.Ordinal))
            {
                return false;
            }

            if (payload.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                return false;
            }

            var providedHash = HashSecret(secret);
            return CryptographicOperations.FixedTimeEquals(payload.TokenHash, providedHash);
        }

        public async Task RevokeAsync(string userId, string? token, CancellationToken cancellationToken)
        {
            if (!TryParseToken(token, out var tokenId, out _))
            {
                return;
            }

            var payload = await GetPayloadAsync(tokenId, cancellationToken);
            if (payload == null || !string.Equals(payload.UserId, userId, StringComparison.Ordinal))
            {
                return;
            }

            await _cache.RemoveAsync(BuildCacheKey(tokenId), cancellationToken);
        }

        private async Task<SessionTokenPayload?> GetPayloadAsync(string tokenId, CancellationToken cancellationToken)
        {
            var cached = await _cache.GetStringAsync(BuildCacheKey(tokenId), cancellationToken);
            if (string.IsNullOrWhiteSpace(cached))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<SessionTokenPayload>(cached, SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize session token payload for {TokenId}", tokenId);
                return null;
            }
        }

        private static byte[] HashSecret(string secret)
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        }

        private static bool TryParseToken(string? token, out string tokenId, out string secret)
        {
            tokenId = string.Empty;
            secret = string.Empty;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            var parts = token.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                return false;
            }

            tokenId = parts[0];
            secret = parts[1];
            return !string.IsNullOrWhiteSpace(tokenId) && !string.IsNullOrWhiteSpace(secret);
        }

        private static string BuildCacheKey(string tokenId) => $"session:{tokenId}";

        private class SessionTokenPayload
        {
            public string UserId { get; set; } = string.Empty;

            public byte[] TokenHash { get; set; } = Array.Empty<byte>();

            public DateTimeOffset ExpiresAt { get; set; }
        }
    }
}
