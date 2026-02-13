using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace SD.ProjectName.WebApp.Services
{
    public class ShippingProviderOptions
    {
        public const string SectionName = "ShippingProviders";

        [MinLength(0)]
        public List<ShippingProviderDefinition> Providers { get; set; } = new();
    }

    public class ShippingProviderDefinition
    {
        [Required]
        [MaxLength(64)]
        public string Id { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string Name { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        [MinLength(0)]
        public List<ShippingProviderServiceDefinition> Services { get; set; } = new();
    }

    public class ShippingProviderServiceDefinition
    {
        [Required]
        [MaxLength(64)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string Name { get; set; } = string.Empty;

        [StringLength(256)]
        public string? TrackingUrlTemplate { get; set; }
    }

    public record ProviderServiceOption(string ProviderId, string ProviderName, string ServiceCode, string ServiceName, string? TrackingUrlTemplate);

    public record ShippingProviderShipmentRequest(
        string ProviderId,
        string ServiceCode,
        string OrderNumber,
        string SubOrderNumber,
        string SellerId,
        DeliveryAddress Address,
        string BuyerEmail,
        string BuyerName,
        string? BuyerPhone,
        decimal DeclaredValue,
        int Quantity,
        string? Reference = null);

    public record ShippingProviderShipmentResult(bool Success, string? TrackingNumber, string? Carrier, string? ProviderReference, string? TrackingUrl, string? Error = null);

    public class ShippingProviderService
    {
        private static readonly Dictionary<string, string> ProviderStatusMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["created"] = OrderStatuses.Preparing,
            ["ready"] = OrderStatuses.Preparing,
            ["label_printed"] = OrderStatuses.Shipped,
            ["in_transit"] = OrderStatuses.Shipped,
            ["out_for_delivery"] = OrderStatuses.Shipped,
            ["delivered"] = OrderStatuses.Delivered,
            ["exception"] = OrderStatuses.Failed,
            ["cancelled"] = OrderStatuses.Cancelled,
            ["canceled"] = OrderStatuses.Cancelled
        };

        private readonly ShippingProviderOptions _options;
        private readonly TimeProvider _clock;
        private readonly ILogger<ShippingProviderService> _logger;

        public ShippingProviderService(ShippingProviderOptions options, TimeProvider clock, ILogger<ShippingProviderService> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public List<ProviderServiceOption> GetProviderServices()
        {
            return _options.Providers
                .Where(p => p.Enabled)
                .SelectMany(p => p.Services.Select(s => new ProviderServiceOption(p.Id, p.Name, s.Code, s.Name, s.TrackingUrlTemplate)))
                .ToList();
        }

        public ShippingProviderDefinition? GetProvider(string? providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return null;
            }

            return _options.Providers.FirstOrDefault(p =>
                p.Enabled && string.Equals(p.Id, providerId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public ShippingProviderServiceDefinition? GetService(string? providerId, string? serviceCode)
        {
            var provider = GetProvider(providerId);
            if (provider == null || provider.Services.Count == 0 || string.IsNullOrWhiteSpace(serviceCode))
            {
                return null;
            }

            return provider.Services.FirstOrDefault(s => string.Equals(s.Code, serviceCode.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public ShippingProviderShipmentResult CreateShipment(ShippingProviderShipmentRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var service = GetService(request.ProviderId, request.ServiceCode);
            if (service == null)
            {
                return new ShippingProviderShipmentResult(false, null, null, null, null, "Shipping provider or service is not configured.");
            }

            var now = _clock.GetUtcNow();
            var referenceSeed = string.IsNullOrWhiteSpace(request.Reference) ? now.ToUnixTimeMilliseconds().ToString() : request.Reference.Trim();
            var providerReference = $"{request.ProviderId}-{request.SubOrderNumber}-{referenceSeed}";
            var tracking = BuildTrackingNumber(request.ProviderId, request.ServiceCode, now);
            var carrier = GetProvider(request.ProviderId)?.Name ?? request.ProviderId;
            var trackingUrl = BuildTrackingUrl(request.ProviderId, request.ServiceCode, tracking);

            _logger.LogInformation("Created shipment via {Provider}/{Service} for {Order}/{SubOrder} with tracking {Tracking}", request.ProviderId, request.ServiceCode, request.OrderNumber, request.SubOrderNumber, tracking);

            return new ShippingProviderShipmentResult(true, tracking, carrier, providerReference, trackingUrl, null);
        }

        public string MapProviderStatus(string? providerStatus)
        {
            if (string.IsNullOrWhiteSpace(providerStatus))
            {
                return OrderStatuses.Shipped;
            }

            return ProviderStatusMap.TryGetValue(providerStatus.Trim(), out var mapped)
                ? mapped
                : OrderStatuses.Shipped;
        }

        public string? BuildTrackingUrl(string? providerId, string? serviceCode, string? trackingNumber)
        {
            if (string.IsNullOrWhiteSpace(trackingNumber))
            {
                return null;
            }

            var service = GetService(providerId, serviceCode);
            if (service == null || string.IsNullOrWhiteSpace(service.TrackingUrlTemplate))
            {
                return null;
            }

            return service.TrackingUrlTemplate!.Replace("{tracking}", Uri.EscapeDataString(trackingNumber.Trim()), StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildTrackingNumber(string providerId, string serviceCode, DateTimeOffset now)
        {
            var providerToken = NormalizeToken(providerId, "PROV");
            var serviceToken = NormalizeToken(serviceCode, "SRV");
            var stamp = now.ToUnixTimeMilliseconds() % 1_000_000;
            return $"{providerToken}-{serviceToken}-{stamp:000000}";
        }

        private static string NormalizeToken(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var letters = value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray();
            var token = new string(letters);
            return token.Length >= 3 ? token[..3] : token.PadRight(3, 'X');
        }
    }
}
