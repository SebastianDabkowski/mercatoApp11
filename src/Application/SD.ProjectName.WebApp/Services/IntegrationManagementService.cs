using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public static class IntegrationTypes
    {
        public const string Payment = "Payment";
        public const string Shipping = "Shipping";
        public const string Erp = "ERP";
    }

    public static class IntegrationStatuses
    {
        public const string Configured = "Configured";
        public const string Healthy = "Healthy";
        public const string Unhealthy = "Unhealthy";
        public const string Disabled = "Disabled";
    }

    public class IntegrationConfiguration
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(64)]
        public string Key { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string Type { get; set; } = IntegrationTypes.Payment;

        [Required]
        [MaxLength(32)]
        public string Environment { get; set; } = "Sandbox";

        [MaxLength(256)]
        public string? ApiKey { get; set; }

        [MaxLength(256)]
        public string? Endpoint { get; set; }

        [MaxLength(128)]
        public string? MerchantId { get; set; }

        [MaxLength(256)]
        public string? CallbackUrl { get; set; }

        [MaxLength(32)]
        public string Status { get; set; } = IntegrationStatuses.Configured;

        [MaxLength(512)]
        public string? LastHealthCheckMessage { get; set; }

        public DateTimeOffset? LastHealthCheckOn { get; set; }

        public bool Enabled { get; set; } = true;

        public DateTimeOffset CreatedOn { get; set; }

        public DateTimeOffset? UpdatedOn { get; set; }
    }

    public record IntegrationView(
        int Id,
        string Key,
        string Name,
        string Type,
        string Environment,
        bool Enabled,
        string Status,
        string? Endpoint,
        string? MerchantId,
        string? CallbackUrl,
        string? ApiKeyPreview,
        DateTimeOffset CreatedOn,
        DateTimeOffset? UpdatedOn,
        DateTimeOffset? LastHealthCheckOn,
        string? LastHealthCheckMessage);

    public record IntegrationUpdateInput(
        int? Id,
        string Key,
        string Name,
        string Type,
        string Environment,
        bool Enabled,
        string? Endpoint,
        string? MerchantId,
        string? CallbackUrl,
        string? ApiKey);

    public record IntegrationOperationResult(bool Success, List<string> Errors)
    {
        public static IntegrationOperationResult Failed(params string[] errors) =>
            new(false, errors.ToList());

        public static IntegrationOperationResult Succeeded() =>
            new(true, new List<string>());
    }

    public record IntegrationHealthResult(bool Success, string Status, string? Message);

    public record IntegrationAvailability(bool Allowed, string? Message);

    public class IntegrationManagementService
    {
        public const string PaymentIntegrationKey = "payment-provider";

        private readonly ApplicationDbContext _dbContext;
        private readonly PaymentProviderOptions _paymentOptions;
        private readonly ShippingProviderOptions _shippingOptions;
        private readonly TimeProvider _clock;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<IntegrationManagementService>? _logger;

        public IntegrationManagementService(
            ApplicationDbContext dbContext,
            PaymentProviderOptions paymentOptions,
            ShippingProviderOptions shippingOptions,
            TimeProvider clock,
            IHostEnvironment environment,
            ILogger<IntegrationManagementService>? logger = null)
        {
            _dbContext = dbContext;
            _paymentOptions = paymentOptions;
            _shippingOptions = shippingOptions;
            _clock = clock;
            _environment = environment;
            _logger = logger;
        }

        public async Task<List<IntegrationView>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            await EnsureDefaultsAsync(cancellationToken);
            var environment = GetEnvironmentName();
            var integrations = await _dbContext.IntegrationConfigurations
                .AsNoTracking()
                .Where(i => i.Environment == environment)
                .OrderBy(i => i.Type)
                .ThenBy(i => i.Name)
                .ToListAsync(cancellationToken);

            return integrations.Select(Map).ToList();
        }

        public async Task<IntegrationView?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            await EnsureDefaultsAsync(cancellationToken);
            var normalizedKey = NormalizeKey(key);
            var environment = GetEnvironmentName();

            var integration = await _dbContext.IntegrationConfigurations.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Key == normalizedKey && i.Environment == environment, cancellationToken);

            return integration == null ? null : Map(integration);
        }

        public async Task<IntegrationView?> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            await EnsureDefaultsAsync(cancellationToken);
            var integration = await _dbContext.IntegrationConfigurations.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
            return integration == null ? null : Map(integration);
        }

        public async Task<IntegrationAvailability> EnsureEnabledAsync(string key, CancellationToken cancellationToken = default)
        {
            var integration = await GetByKeyAsync(key, cancellationToken);
            if (integration == null)
            {
                return new IntegrationAvailability(false, "Integration is not configured.");
            }

            if (!integration.Enabled)
            {
                return new IntegrationAvailability(false, $"{integration.Name} integration is disabled.");
            }

            if (string.Equals(integration.Status, IntegrationStatuses.Unhealthy, StringComparison.OrdinalIgnoreCase))
            {
                return new IntegrationAvailability(false, integration.LastHealthCheckMessage ?? $"{integration.Name} failed its last health check.");
            }

            return new IntegrationAvailability(true, integration.Status);
        }

        public async Task<IntegrationOperationResult> SaveAsync(IntegrationUpdateInput input, CancellationToken cancellationToken = default)
        {
            if (input == null)
            {
                return IntegrationOperationResult.Failed("Integration input is required.");
            }

            var errors = new List<string>();
            var normalizedKey = NormalizeKey(input.Key);
            var normalizedName = NormalizeName(input.Name);
            var normalizedType = NormalizeType(input.Type);
            var normalizedEnvironment = NormalizeEnvironment(input.Environment);

            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                errors.Add("Integration key is required.");
            }

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                errors.Add("Integration name is required.");
            }

            if (string.IsNullOrWhiteSpace(normalizedType))
            {
                errors.Add("Integration type is required.");
            }

            if (string.IsNullOrWhiteSpace(normalizedEnvironment))
            {
                errors.Add("Environment is required.");
            }

            if (errors.Count > 0)
            {
                return IntegrationOperationResult.Failed(errors.ToArray());
            }

            var existing = input.Id.HasValue
                ? await _dbContext.IntegrationConfigurations.FirstOrDefaultAsync(i => i.Id == input.Id.Value, cancellationToken)
                : await _dbContext.IntegrationConfigurations.FirstOrDefaultAsync(i => i.Key == normalizedKey && i.Environment == normalizedEnvironment, cancellationToken);

            var now = _clock.GetUtcNow();
            if (existing == null)
            {
                var integration = new IntegrationConfiguration
                {
                    Key = normalizedKey,
                    Name = normalizedName,
                    Type = normalizedType,
                    Environment = normalizedEnvironment,
                    Enabled = input.Enabled,
                    Endpoint = NormalizeInput(input.Endpoint),
                    MerchantId = NormalizeInput(input.MerchantId),
                    CallbackUrl = NormalizeInput(input.CallbackUrl),
                    ApiKey = NormalizeSecret(input.ApiKey),
                    Status = IntegrationStatuses.Configured,
                    CreatedOn = now,
                    UpdatedOn = now
                };

                await _dbContext.IntegrationConfigurations.AddAsync(integration, cancellationToken);
            }
            else
            {
                existing.Name = normalizedName;
                existing.Type = normalizedType;
                existing.Environment = normalizedEnvironment;
                existing.Enabled = input.Enabled;
                existing.Endpoint = NormalizeInput(input.Endpoint);
                existing.MerchantId = NormalizeInput(input.MerchantId);
                existing.CallbackUrl = NormalizeInput(input.CallbackUrl);
                if (!string.IsNullOrWhiteSpace(input.ApiKey))
                {
                    existing.ApiKey = NormalizeSecret(input.ApiKey);
                }
                existing.UpdatedOn = now;
                if (!existing.Enabled)
                {
                    existing.Status = IntegrationStatuses.Disabled;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return IntegrationOperationResult.Succeeded();
        }

        public async Task<IntegrationHealthResult> RunHealthCheckAsync(int integrationId, CancellationToken cancellationToken = default)
        {
            var integration = await _dbContext.IntegrationConfigurations.FirstOrDefaultAsync(i => i.Id == integrationId, cancellationToken);
            if (integration == null)
            {
                return new IntegrationHealthResult(false, IntegrationStatuses.Unhealthy, "Integration not found.");
            }

            if (!integration.Enabled)
            {
                integration.Status = IntegrationStatuses.Disabled;
                integration.LastHealthCheckMessage = "Integration is disabled.";
                integration.LastHealthCheckOn = _clock.GetUtcNow();
                integration.UpdatedOn = integration.LastHealthCheckOn;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new IntegrationHealthResult(false, IntegrationStatuses.Disabled, integration.LastHealthCheckMessage);
            }

            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(integration.Endpoint))
            {
                errors.Add("Endpoint is required for health checks.");
            }
            else if (!Uri.TryCreate(integration.Endpoint, UriKind.Absolute, out _))
            {
                errors.Add("Endpoint URL is invalid.");
            }

            if (string.Equals(integration.Type, IntegrationTypes.Payment, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(integration.ApiKey))
            {
                errors.Add("API key is required for payment providers.");
            }

            if (string.Equals(integration.Type, IntegrationTypes.Shipping, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(integration.MerchantId))
            {
                errors.Add("Merchant ID is required for shipping providers.");
            }

            var status = errors.Count == 0 ? IntegrationStatuses.Healthy : IntegrationStatuses.Unhealthy;
            var message = errors.Count == 0 ? "Integration responded successfully." : string.Join(" ", errors);

            integration.Status = status;
            integration.LastHealthCheckMessage = message;
            integration.LastHealthCheckOn = _clock.GetUtcNow();
            integration.UpdatedOn = integration.LastHealthCheckOn;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new IntegrationHealthResult(errors.Count == 0, status, message);
        }

        public static string BuildShippingKey(string providerId) =>
            $"shipping-{NormalizeKey(providerId)}";

        private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
        {
            var environment = GetEnvironmentName();
            var existing = await _dbContext.IntegrationConfigurations
                .Where(i => i.Environment == environment)
                .ToListAsync(cancellationToken);

            var now = _clock.GetUtcNow();
            var added = false;
            if (!existing.Any(i => string.Equals(i.Key, PaymentIntegrationKey, StringComparison.OrdinalIgnoreCase)))
            {
                await _dbContext.IntegrationConfigurations.AddAsync(new IntegrationConfiguration
                {
                    Key = PaymentIntegrationKey,
                    Name = string.IsNullOrWhiteSpace(_paymentOptions.ProviderName) ? "Payment Provider" : _paymentOptions.ProviderName,
                    Type = IntegrationTypes.Payment,
                    Environment = environment,
                    Enabled = true,
                    Status = IntegrationStatuses.Configured,
                    Endpoint = "https://payment-provider.example/api",
                    CallbackUrl = "https://payment-provider.example/callback",
                    CreatedOn = now,
                    UpdatedOn = now
                }, cancellationToken);
                added = true;
            }

            foreach (var provider in _shippingOptions.Providers ?? new List<ShippingProviderDefinition>())
            {
                if (string.IsNullOrWhiteSpace(provider?.Id))
                {
                    continue;
                }

                var key = BuildShippingKey(provider.Id);
                if (existing.Any(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                await _dbContext.IntegrationConfigurations.AddAsync(new IntegrationConfiguration
                {
                    Key = key,
                    Name = provider.Name,
                    Type = IntegrationTypes.Shipping,
                    Environment = environment,
                    Enabled = provider.Enabled,
                    Status = provider.Enabled ? IntegrationStatuses.Configured : IntegrationStatuses.Disabled,
                    Endpoint = "https://shipping-provider.example/api",
                    CallbackUrl = "https://shipping-provider.example/callback",
                    CreatedOn = now,
                    UpdatedOn = now
                }, cancellationToken);
                added = true;
            }

            if (added)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        private IntegrationView Map(IntegrationConfiguration entity) =>
            new IntegrationView(
                entity.Id,
                entity.Key,
                entity.Name,
                entity.Type,
                entity.Environment,
                entity.Enabled,
                entity.Status,
                entity.Endpoint,
                entity.MerchantId,
                entity.CallbackUrl,
                MaskSecret(entity.ApiKey),
                entity.CreatedOn,
                entity.UpdatedOn,
                entity.LastHealthCheckOn,
                entity.LastHealthCheckMessage);

        private string GetEnvironmentName()
        {
            return NormalizeEnvironment(_environment.EnvironmentName);
        }

        private static string NormalizeKey(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

        private static string NormalizeName(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        private static string NormalizeType(string value)
        {
            var normalized = NormalizeName(value);
            return string.IsNullOrWhiteSpace(normalized) ? IntegrationTypes.Payment : normalized;
        }

        private static string NormalizeEnvironment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Sandbox";
            }

            var normalized = value.Trim();
            if (normalized.Contains("prod", StringComparison.OrdinalIgnoreCase))
            {
                return "Production";
            }

            if (normalized.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                return "Test";
            }

            return "Sandbox";
        }

        private static string? NormalizeInput(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? NormalizeSecret(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? MaskSecret(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.Length <= 4)
            {
                return new string('*', trimmed.Length);
            }

            return $"{new string('*', Math.Min(6, trimmed.Length - 2))}{trimmed[^2..]}";
        }
    }
}
