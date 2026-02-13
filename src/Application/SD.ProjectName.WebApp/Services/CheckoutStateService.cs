using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SD.ProjectName.WebApp.Services
{
    public class CheckoutOptions
    {
        public const string SectionName = "Checkout";

        [Required]
        [MaxLength(64)]
        public string CookieName { get; set; } = ".SD.Checkout";

        [Range(1, 30)]
        public int StateLifespanDays { get; set; } = 3;

        public List<string> DefaultShippingMethods { get; set; } = new() { "Standard", "Express" };

        public List<PaymentMethodOption> PaymentMethods { get; set; } = new()
        {
            new()
            {
                Id = "card",
                Label = "Credit or debit card",
                Provider = "MockPay",
                RequiresRedirect = true
            },
            new()
            {
                Id = "wallet",
                Label = "Wallet",
                Provider = "MockPay",
                RequiresRedirect = false
            },
            new()
            {
                Id = "cod",
                Label = "Cash on delivery",
                Provider = "Manual",
                RequiresRedirect = false
            }
        };
    }

    public record DeliveryAddress(
        string Recipient,
        string Line1,
        string? Line2,
        string City,
        string State,
        string PostalCode,
        string Country,
        string? Phone);

    public record PaymentMethodOption
    {
        [Required]
        [MaxLength(50)]
        public string Id { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Label { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Provider { get; set; } = string.Empty;

        public bool RequiresRedirect { get; set; }
    }

    public enum CheckoutPaymentStatus
    {
        None = 0,
        Pending = 1,
        Confirmed = 2,
        Failed = 3,
        Canceled = 4
    }

    public record CheckoutState(
        string? SavedAddressKey,
        DeliveryAddress Address,
        DateTimeOffset SavedAt,
        Dictionary<string, string>? ShippingSelections = null,
        string? PaymentMethod = null,
        CheckoutPaymentStatus PaymentStatus = CheckoutPaymentStatus.None,
        string? PaymentReference = null);

    public class CheckoutStateService
    {
        private readonly CheckoutOptions _options;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public CheckoutStateService(CheckoutOptions options)
        {
            _options = options;
        }

        public CheckoutState? Get(HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (!context.Request.Cookies.TryGetValue(_options.CookieName, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                var state = JsonSerializer.Deserialize<CheckoutState>(value, _serializerOptions);
                return state == null ? null : NormalizeState(state);
            }
            catch
            {
                return null;
            }
        }

        public CheckoutState Save(HttpContext context, string? savedAddressKey, DeliveryAddress address)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            var existing = Get(context);
            var shouldResetShipping = existing?.Address == null || !AddressesEqual(existing.Address, address);

            var shippingSelections = shouldResetShipping
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(existing!.ShippingSelections ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

            var state = new CheckoutState(
                savedAddressKey,
                address,
                DateTimeOffset.UtcNow,
                shippingSelections,
                shouldResetShipping ? null : existing?.PaymentMethod,
                shouldResetShipping ? CheckoutPaymentStatus.None : existing?.PaymentStatus ?? CheckoutPaymentStatus.None,
                shouldResetShipping ? null : existing?.PaymentReference);

            SaveState(context, state);
            return state;
        }

        public CheckoutState SaveShippingSelections(HttpContext context, IDictionary<string, string> selections)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var state = Get(context);
            if (state == null)
            {
                throw new InvalidOperationException("Delivery address is required before selecting shipping.");
            }

            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (selections != null)
            {
                foreach (var pair in selections)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    {
                        continue;
                    }

                    normalized[pair.Key] = pair.Value.Trim();
                }
            }

            var updated = state with
            {
                ShippingSelections = normalized,
                PaymentMethod = null,
                PaymentStatus = CheckoutPaymentStatus.None,
                PaymentReference = null,
                SavedAt = DateTimeOffset.UtcNow
            };

            SaveState(context, updated);
            return updated;
        }

        public CheckoutState SavePaymentSelection(HttpContext context, string methodId, CheckoutPaymentStatus status, string? reference)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (string.IsNullOrWhiteSpace(methodId))
            {
                throw new ArgumentException("Payment method is required.", nameof(methodId));
            }

            var state = Get(context);
            if (state == null)
            {
                throw new InvalidOperationException("Delivery address is required before selecting payment.");
            }

            var updated = state with
            {
                PaymentMethod = methodId.Trim(),
                PaymentStatus = status,
                PaymentReference = reference,
                SavedAt = DateTimeOffset.UtcNow
            };

            SaveState(context, updated);
            return updated;
        }

        public void Clear(HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.Response.Cookies.Delete(_options.CookieName, BuildCookieOptions());
        }

        private CookieOptions BuildCookieOptions()
        {
            return new CookieOptions
            {
                HttpOnly = true,
                IsEssential = false,
                SameSite = SameSiteMode.Lax,
                Secure = true,
                Expires = DateTimeOffset.UtcNow.AddDays(_options.StateLifespanDays),
                Path = "/"
            };
        }

        private void SaveState(HttpContext context, CheckoutState state)
        {
            var normalized = NormalizeState(state);
            var value = JsonSerializer.Serialize(normalized, _serializerOptions);
            context.Response.Cookies.Append(_options.CookieName, value, BuildCookieOptions());
        }

        private static CheckoutState NormalizeState(CheckoutState state)
        {
            var shipping = state.ShippingSelections ?? new Dictionary<string, string>();
            if (shipping is not Dictionary<string, string> normalized)
            {
                normalized = new Dictionary<string, string>(shipping, StringComparer.OrdinalIgnoreCase);
            }
            else if (normalized.Comparer != StringComparer.OrdinalIgnoreCase)
            {
                normalized = new Dictionary<string, string>(normalized, StringComparer.OrdinalIgnoreCase);
            }

            return state with
            {
                ShippingSelections = normalized,
                PaymentMethod = string.IsNullOrWhiteSpace(state.PaymentMethod) ? null : state.PaymentMethod.Trim()
            };
        }

        private static bool AddressesEqual(DeliveryAddress first, DeliveryAddress second)
        {
            return string.Equals(first.Recipient, second.Recipient, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.Line1, second.Line1, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.Line2, second.Line2, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.City, second.City, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.State, second.State, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.PostalCode, second.PostalCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.Country, second.Country, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.Phone, second.Phone, StringComparison.OrdinalIgnoreCase);
        }
    }
}
