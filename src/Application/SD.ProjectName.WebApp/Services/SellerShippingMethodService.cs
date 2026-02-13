using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public class SellerShippingMethodService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _clock;
        private readonly ShippingProviderService _shippingProviderService;

        public SellerShippingMethodService(ApplicationDbContext dbContext, TimeProvider clock, ShippingProviderService shippingProviderService)
        {
            _dbContext = dbContext;
            _clock = clock;
            _shippingProviderService = shippingProviderService;
        }

        public async Task<List<SellerShippingMethod>> GetForStoreAsync(string storeOwnerId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(storeOwnerId))
            {
                return new List<SellerShippingMethod>();
            }

            return await _dbContext.SellerShippingMethods
                .Where(m => m.StoreOwnerId == storeOwnerId)
                .OrderByDescending(m => m.CreatedOn)
                .ToListAsync(cancellationToken);
        }

        public async Task<Dictionary<string, List<SellerShippingMethod>>> GetAvailableForSellersAsync(IEnumerable<string> sellerIds, string? buyerCountry, CancellationToken cancellationToken = default)
        {
            var ids = sellerIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ids.Count == 0)
            {
                return new Dictionary<string, List<SellerShippingMethod>>(StringComparer.OrdinalIgnoreCase);
            }

            var methods = await _dbContext.SellerShippingMethods
                .Where(m => ids.Contains(m.StoreOwnerId) && !m.IsDeleted && m.IsActive)
                .ToListAsync(cancellationToken);

            methods = methods
                .Where(IsMethodEnabled)
                .ToList();

            var grouped = new Dictionary<string, List<SellerShippingMethod>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in methods.GroupBy(m => m.StoreOwnerId, StringComparer.OrdinalIgnoreCase))
            {
                var available = group
                    .Where(m => IsAvailableForCountry(m.Availability, buyerCountry))
                    .OrderBy(m => m.Name)
                    .ToList();
                grouped[group.Key] = available;
            }

            return grouped;
        }

        public async Task<SellerShippingMethod?> GetAsync(Guid id, string storeOwnerId, CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty || string.IsNullOrWhiteSpace(storeOwnerId))
            {
                return null;
            }

            return await _dbContext.SellerShippingMethods
                .FirstOrDefaultAsync(m => m.Id == id && m.StoreOwnerId == storeOwnerId, cancellationToken);
        }

        public async Task<SellerShippingMethod?> SaveAsync(
            string storeOwnerId,
            Guid? id,
            string name,
            string? description,
            decimal baseCost,
            string? deliveryEstimate,
            string? availability,
            bool isActive,
            string? providerId = null,
            string? providerServiceCode = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(storeOwnerId))
            {
                return null;
            }

            var now = _clock.GetUtcNow();
            SellerShippingMethod method;
            var normalizedCost = NormalizeCost(baseCost);
            var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            var normalizedEstimate = string.IsNullOrWhiteSpace(deliveryEstimate) ? null : deliveryEstimate.Trim();
            var normalizedProviderId = NormalizeProvider(providerId);
            var normalizedServiceCode = NormalizeProvider(providerServiceCode);
            var providerService = normalizedProviderId == null || normalizedServiceCode == null
                ? null
                : _shippingProviderService.GetService(normalizedProviderId, normalizedServiceCode);
            if (normalizedProviderId != null && normalizedServiceCode != null && providerService == null)
            {
                return null;
            }

            if (id.HasValue && id.Value != Guid.Empty)
            {
                method = await GetAsync(id.Value, storeOwnerId, cancellationToken) ?? throw new InvalidOperationException("Shipping method not found.");
                method.Name = name.Trim();
                method.Description = normalizedDescription;
                method.BaseCost = normalizedCost;
                method.DeliveryEstimate = normalizedEstimate;
                method.Availability = NormalizeAvailability(availability);
                method.IsActive = isActive && !method.IsDeleted;
                method.UpdatedOn = now;
                method.ProviderId = normalizedProviderId;
                method.ProviderServiceCode = providerService?.Code ?? normalizedServiceCode;
            }
            else
            {
                method = new SellerShippingMethod
                {
                    Id = Guid.NewGuid(),
                    StoreOwnerId = storeOwnerId,
                    Name = name.Trim(),
                    Description = normalizedDescription,
                    BaseCost = normalizedCost,
                    DeliveryEstimate = normalizedEstimate,
                    Availability = NormalizeAvailability(availability),
                    ProviderId = normalizedProviderId,
                    ProviderServiceCode = providerService?.Code ?? normalizedServiceCode,
                    IsActive = isActive,
                    IsDeleted = false,
                    CreatedOn = now,
                    UpdatedOn = now
                };
                await _dbContext.SellerShippingMethods.AddAsync(method, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return method;
        }

        public async Task ArchiveAsync(Guid id, string storeOwnerId, CancellationToken cancellationToken = default)
        {
            var method = await GetAsync(id, storeOwnerId, cancellationToken);
            if (method == null)
            {
                return;
            }

            method.IsDeleted = true;
            method.IsActive = false;
            method.UpdatedOn = _clock.GetUtcNow();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public static bool IsAvailableForCountry(string? availability, string? country)
        {
            if (string.IsNullOrWhiteSpace(availability))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(country))
            {
                return false;
            }

            var normalizedCountry = country.Trim();
            var tokens = availability.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var token in tokens)
            {
                if (string.Equals(normalizedCountry, token, StringComparison.OrdinalIgnoreCase) ||
                    normalizedCountry.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsMethodEnabled(SellerShippingMethod method)
        {
            if (string.IsNullOrWhiteSpace(method.ProviderId))
            {
                return true;
            }

            return _shippingProviderService.GetService(method.ProviderId, method.ProviderServiceCode) != null;
        }

        private static string? NormalizeProvider(string? provider)
        {
            return string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        }

        private static string? NormalizeAvailability(string? availability)
        {
            if (string.IsNullOrWhiteSpace(availability))
            {
                return null;
            }

            var tokens = availability
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return tokens.Count == 0 ? null : string.Join(", ", tokens);
        }

        private static decimal NormalizeCost(decimal cost)
        {
            var normalized = Math.Max(0, cost);
            return Math.Round(normalized, 2, MidpointRounding.AwayFromZero);
        }
    }
}
