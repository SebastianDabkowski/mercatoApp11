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

    public record CheckoutState(string? SavedAddressKey, DeliveryAddress Address, DateTimeOffset SavedAt);

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
                return JsonSerializer.Deserialize<CheckoutState>(value, _serializerOptions);
            }
            catch
            {
                return null;
            }
        }

        public void Save(HttpContext context, string? savedAddressKey, DeliveryAddress address)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            var state = new CheckoutState(savedAddressKey, address, DateTimeOffset.UtcNow);
            var value = JsonSerializer.Serialize(state, _serializerOptions);

            context.Response.Cookies.Append(_options.CookieName, value, BuildCookieOptions());
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
    }
}
