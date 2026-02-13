using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
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

        public bool SupportsLabelCreation { get; set; } = true;

        public int LabelRetentionDays { get; set; } = 30;
    }

    public record ProviderServiceOption(string ProviderId, string ProviderName, string ServiceCode, string ServiceName, string? TrackingUrlTemplate);

    public record ShippingProviderLabel(string FileName, string ContentType, byte[] Content, DateTimeOffset CreatedOn, DateTimeOffset? ExpiresOn = null);

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

    public record ShippingProviderShipmentResult(bool Success, string? TrackingNumber, string? Carrier, string? ProviderReference, string? TrackingUrl, string? Error = null, ShippingProviderLabel? Label = null);

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
            ShippingProviderLabel? label = null;
            if (service.SupportsLabelCreation)
            {
                label = BuildShippingLabel(request, tracking, carrier, providerReference, now, service.LabelRetentionDays);
                if (label == null)
                {
                    return new ShippingProviderShipmentResult(false, null, null, null, null, "Failed to generate shipping label.");
                }
            }

            _logger.LogInformation("Created shipment via {Provider}/{Service} for {Order}/{SubOrder} with tracking {Tracking}", request.ProviderId, request.ServiceCode, request.OrderNumber, request.SubOrderNumber, tracking);

            return new ShippingProviderShipmentResult(true, tracking, carrier, providerReference, trackingUrl, null, label);
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

        private ShippingProviderLabel? BuildShippingLabel(ShippingProviderShipmentRequest request, string tracking, string carrier, string providerReference, DateTimeOffset now, int retentionDays)
        {
            try
            {
                DateTimeOffset? expiresOn = retentionDays > 0 ? now.AddDays(retentionDays) : null;
                var fileName = string.IsNullOrWhiteSpace(request.SubOrderNumber) ? "shipping-label.pdf" : $"{request.SubOrderNumber.Trim()}-label.pdf";
                var content = RenderLabelPdf(request, tracking, carrier, providerReference);
                if (content == null || content.Length == 0)
                {
                    return null;
                }

                return new ShippingProviderLabel(fileName, "application/pdf", content, now, expiresOn);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to render shipping label for {Order}/{SubOrder} via {Provider}/{Service}", request.OrderNumber, request.SubOrderNumber, request.ProviderId, request.ServiceCode);
                return null;
            }
        }

        protected virtual byte[] RenderLabelPdf(ShippingProviderShipmentRequest request, string trackingNumber, string carrier, string providerReference)
        {
            var lines = new List<string>
            {
                "Mercato Shipping Label",
                $"Order: {request.OrderNumber}",
                $"Shipment: {request.SubOrderNumber}",
                $"Carrier: {carrier}",
                $"Service: {request.ServiceCode}",
                $"Tracking: {trackingNumber}",
                $"Reference: {providerReference}",
                "Ship to:",
                request.Address.Recipient,
                request.Address.Line1
            };

            if (!string.IsNullOrWhiteSpace(request.Address.Line2))
            {
                lines.Add(request.Address.Line2);
            }

            lines.Add($"{request.Address.City} {request.Address.State} {request.Address.PostalCode}".Trim());
            lines.Add(request.Address.Country);

            if (!string.IsNullOrWhiteSpace(request.BuyerPhone))
            {
                lines.Add($"Phone: {request.BuyerPhone}");
            }

            if (!string.IsNullOrWhiteSpace(request.BuyerEmail))
            {
                lines.Add($"Email: {request.BuyerEmail}");
            }

            lines.Add($"Declared value: {request.DeclaredValue.ToString("F2", CultureInfo.InvariantCulture)}");
            return RenderPdfFromLines(lines);
        }

        private static byte[] RenderPdfFromLines(IEnumerable<string> lines)
        {
            var objects = new List<string>
            {
                "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj",
                "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj",
                "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>"
            };

            var content = BuildPdfContent(lines);
            var contentBytes = Encoding.ASCII.GetBytes(content);
            objects.Add($"4 0 obj << /Length {contentBytes.Length} >> stream\n{content}\nendstream endobj");
            objects.Add("5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj");

            using var stream = new MemoryStream();
            var offsets = new List<long> { 0 };

            void WriteLine(string line)
            {
                var bytes = Encoding.ASCII.GetBytes(line + "\n");
                stream.Write(bytes, 0, bytes.Length);
            }

            WriteLine("%PDF-1.4");

            foreach (var obj in objects)
            {
                offsets.Add(stream.Position);
                WriteLine(obj);
            }

            var xrefPosition = stream.Position;
            WriteLine("xref");
            WriteLine($"0 {objects.Count + 1}");
            WriteLine("0000000000 65535 f ");
            foreach (var offset in offsets.Skip(1))
            {
                WriteLine($"{offset:0000000000} 00000 n ");
            }

            WriteLine($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>");
            WriteLine("startxref");
            WriteLine(xrefPosition.ToString(CultureInfo.InvariantCulture));
            WriteLine("%%EOF");

            return stream.ToArray();
        }

        private static string BuildPdfContent(IEnumerable<string> lines)
        {
            var builder = new StringBuilder();
            builder.AppendLine("BT");
            builder.AppendLine("/F1 12 Tf");
            var y = 760;
            foreach (var line in lines)
            {
                builder.AppendLine($"1 0 0 1 72 {y} Tm");
                builder.AppendLine($"({EscapePdfText(line)}) Tj");
                y -= 16;
            }

            builder.AppendLine("ET");
            return builder.ToString();
        }

        private static string EscapePdfText(string text) =>
            text
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal);

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
