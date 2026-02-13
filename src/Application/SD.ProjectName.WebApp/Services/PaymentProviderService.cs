using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace SD.ProjectName.WebApp.Services
{
    public class PaymentProviderOptions
    {
        public const string SectionName = "PaymentProvider";

        [Required]
        [MinLength(32)]
        [MaxLength(128)]
        public string SigningKey { get; set; } = "change-me-to-a-very-long-secure-signing-key";

        [Range(1, 120)]
        public int TokenLifetimeMinutes { get; set; } = 15;

        [Range(4, 8)]
        public int BlikCodeLength { get; set; } = 6;

        [Required]
        [MaxLength(100)]
        public string ProviderName { get; set; } = "SecurePay";
    }

    public record PaymentRedirectRequest(string MethodId, decimal Amount, string Currency, string ReturnUrl, string CancelUrl);

    public record PaymentRedirectResult(string RedirectUrl, string CancelUrl, string PaymentReference);

    public record PaymentAuthorizationResult(CheckoutPaymentStatus Status, string PaymentReference, string? MethodId = null, string? Error = null);

    public class PaymentProviderService
    {
        private readonly PaymentProviderOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly byte[] _signingKey;

        public PaymentProviderService(PaymentProviderOptions options, TimeProvider timeProvider)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _signingKey = Encoding.UTF8.GetBytes(_options.SigningKey);
        }

        public PaymentRedirectResult CreateRedirectPayment(PaymentRedirectRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var reference = BuildReference(request.MethodId);
            var successToken = BuildToken(reference, request.MethodId, CheckoutPaymentStatus.Confirmed, request.Amount, request.Currency);
            var cancelToken = BuildToken(reference, request.MethodId, CheckoutPaymentStatus.Canceled, request.Amount, request.Currency);

            var redirectUrl = AppendToken(request.ReturnUrl, successToken);
            var cancelUrl = AppendToken(request.CancelUrl, cancelToken);

            return new PaymentRedirectResult(redirectUrl, cancelUrl, reference);
        }

        public PaymentAuthorizationResult ValidateReturn(string token, decimal expectedAmount, string expectedCurrency, string? expectedMethodId = null)
        {
            var payload = ParseToken(token);
            if (payload == null)
            {
                return new PaymentAuthorizationResult(CheckoutPaymentStatus.Failed, BuildReference(expectedMethodId ?? "payment", token ?? string.Empty), expectedMethodId, "Invalid payment token.");
            }

            if (payload.ExpiresAt < _timeProvider.GetUtcNow())
            {
                return new PaymentAuthorizationResult(CheckoutPaymentStatus.Failed, payload.Reference, payload.MethodId, "Payment session expired.");
            }

            if (!string.IsNullOrWhiteSpace(expectedMethodId) && !string.Equals(payload.MethodId, expectedMethodId, StringComparison.OrdinalIgnoreCase))
            {
                return new PaymentAuthorizationResult(CheckoutPaymentStatus.Failed, payload.Reference, payload.MethodId, "Payment method mismatch.");
            }

            if (!string.Equals(payload.Currency, expectedCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return new PaymentAuthorizationResult(CheckoutPaymentStatus.Failed, payload.Reference, payload.MethodId, "Currency mismatch.");
            }

            if (Math.Round(payload.Amount, 2, MidpointRounding.AwayFromZero) != Math.Round(expectedAmount, 2, MidpointRounding.AwayFromZero))
            {
                return new PaymentAuthorizationResult(CheckoutPaymentStatus.Failed, payload.Reference, payload.MethodId, "Payment amount mismatch.");
            }

            return new PaymentAuthorizationResult(payload.Status, payload.Reference, payload.MethodId);
        }

        public PaymentAuthorizationResult AuthorizeBlik(string methodId, decimal amount, string currency, string? blikCode)
        {
            var normalizedMethod = string.IsNullOrWhiteSpace(methodId) ? "blik" : methodId.Trim();
            var reference = BuildReference(normalizedMethod);
            var code = blikCode?.Trim() ?? string.Empty;

            if (code.Length != _options.BlikCodeLength || !code.All(char.IsDigit))
            {
                return new PaymentAuthorizationResult(
                    CheckoutPaymentStatus.Failed,
                    reference,
                    normalizedMethod,
                    $"Enter a valid {_options.BlikCodeLength}-digit BLIK code.");
            }

            // Simple simulation: codes ending with 0 are rejected.
            var status = code.EndsWith('0') ? CheckoutPaymentStatus.Failed : CheckoutPaymentStatus.Confirmed;
            var error = status == CheckoutPaymentStatus.Failed ? "BLIK code was rejected by the bank." : null;
            return new PaymentAuthorizationResult(status, reference, normalizedMethod, error);
        }

        private string BuildToken(string reference, string methodId, CheckoutPaymentStatus status, decimal amount, string currency)
        {
            var expiresAt = _timeProvider.GetUtcNow().AddMinutes(_options.TokenLifetimeMinutes);
            var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "PLN" : currency.Trim().ToUpperInvariant();
            var payload = string.Join(
                '|',
                reference,
                methodId,
                status.ToString(),
                amount.ToString(CultureInfo.InvariantCulture),
                normalizedCurrency,
                expiresAt.ToUnixTimeSeconds());
            var signature = Sign(payload);
            var token = string.Join('|', payload, signature);
            return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        }

        private PaymentReturnPayload? ParseToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            try
            {
                var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
                var parts = decoded.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 7)
                {
                    return null;
                }

                var signature = parts[^1];
                var payload = string.Join('|', parts.Take(parts.Length - 1));
                if (!Verify(payload, signature))
                {
                    return null;
                }

                if (!Enum.TryParse(parts[2], ignoreCase: true, out CheckoutPaymentStatus status))
                {
                    return null;
                }

                if (!decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                {
                    return null;
                }

                if (!long.TryParse(parts[5], out var expiresSeconds))
                {
                    return null;
                }

                var expires = DateTimeOffset.FromUnixTimeSeconds(expiresSeconds);
                return new PaymentReturnPayload(parts[0], parts[1], status, amount, parts[4], expires);
            }
            catch
            {
                return null;
            }
        }

        private string BuildReference(string methodId, string? seed = null)
        {
            var prefix = string.IsNullOrWhiteSpace(methodId) ? "pay" : methodId.Trim().ToLowerInvariant();
            var referenceSeed = string.IsNullOrWhiteSpace(seed)
                ? Guid.NewGuid().ToString("N")
                : WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..16];
            return $"{prefix}-{referenceSeed}";
        }

        private string Sign(string payload)
        {
            using var hmac = new HMACSHA256(_signingKey);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return WebEncoders.Base64UrlEncode(hash);
        }

        private bool Verify(string payload, string signature)
        {
            try
            {
                var provided = WebEncoders.Base64UrlDecode(signature);
                using var hmac = new HMACSHA256(_signingKey);
                var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                return CryptographicOperations.FixedTimeEquals(expected, provided);
            }
            catch
            {
                return false;
            }
        }

        private static string AppendToken(string url, string token)
        {
            var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{url}{separator}providerToken={token}";
        }

        private record PaymentReturnPayload(string Reference, string MethodId, CheckoutPaymentStatus Status, decimal Amount, string Currency, DateTimeOffset ExpiresAt);
    }
}
