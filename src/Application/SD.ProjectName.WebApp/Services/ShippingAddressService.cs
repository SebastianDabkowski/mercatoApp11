using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Pages.Checkout;

namespace SD.ProjectName.WebApp.Services
{
    public class ShippingAddressOptions
    {
        public const string SectionName = "ShippingAddresses";

        public List<string> AllowedCountries { get; set; } = new();
    }

    public enum ShippingAddressDeleteResult
    {
        NotFound = 0,
        BlockedByActiveOrder = 1,
        Deleted = 2
    }

    public class ShippingAddressService
    {
        private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            OrderStatuses.New,
            OrderStatuses.Paid,
            OrderStatuses.Preparing,
            OrderStatuses.Shipped
        };

        private readonly ApplicationDbContext _dbContext;
        private readonly ShippingAddressOptions _options;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ShippingAddressService(ApplicationDbContext dbContext, ShippingAddressOptions options)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _options = options ?? new ShippingAddressOptions();
        }

        public bool IsCountrySupported(string country)
        {
            var trimmed = (country ?? string.Empty).Trim();
            return _options.AllowedCountries == null
                || _options.AllowedCountries.Count == 0
                || _options.AllowedCountries.Any(c => c.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<ShippingAddress>> GetAddressesAsync(string userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new List<ShippingAddress>();
            }

            return await _dbContext.ShippingAddresses.AsNoTracking()
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.IsDefault)
                .ThenBy(a => a.CreatedOn)
                .ToListAsync(cancellationToken);
        }

        public async Task<ShippingAddress?> GetAsync(string userId, int id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            return await _dbContext.ShippingAddresses
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, cancellationToken);
        }

        public async Task<ShippingAddress> UpsertAsync(string userId, AddressForm input, bool makeDefault, CancellationToken cancellationToken = default)
        {
            ValidateAddress(input);

            var normalized = Normalize(input);
            var existing = (await _dbContext.ShippingAddresses
                    .Where(a => a.UserId == userId)
                    .ToListAsync(cancellationToken))
                .FirstOrDefault(a => AddressesEqual(ToDeliveryAddress(a), normalized));

            if (existing != null)
            {
                Apply(normalized, existing);
                existing.UpdatedOn = DateTimeOffset.UtcNow;
                if (makeDefault)
                {
                    await SetDefaultInternalAsync(userId, existing, cancellationToken);
                }
                else
                {
                    await EnsureDefaultAsync(userId, existing, cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                return existing;
            }

            var address = new ShippingAddress
            {
                UserId = userId,
                Recipient = normalized.Recipient,
                Line1 = normalized.Line1,
                Line2 = normalized.Line2,
                City = normalized.City,
                State = normalized.State,
                PostalCode = normalized.PostalCode,
                Country = normalized.Country ?? string.Empty,
                Phone = normalized.Phone ?? string.Empty,
                CreatedOn = DateTimeOffset.UtcNow,
                UpdatedOn = DateTimeOffset.UtcNow,
                IsDefault = false
            };

            _dbContext.ShippingAddresses.Add(address);
            if (makeDefault)
            {
                await SetDefaultInternalAsync(userId, address, cancellationToken);
            }
            else
            {
                await EnsureDefaultAsync(userId, address, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return address;
        }

        public async Task<ShippingAddress?> UpdateAsync(string userId, int id, AddressForm input, bool makeDefault, CancellationToken cancellationToken = default)
        {
            ValidateAddress(input);
            var address = await GetAsync(userId, id, cancellationToken);
            if (address == null)
            {
                return null;
            }

            var normalized = Normalize(input);
            Apply(normalized, address);
            address.UpdatedOn = DateTimeOffset.UtcNow;

            if (makeDefault)
            {
                await SetDefaultInternalAsync(userId, address, cancellationToken);
            }
            else
            {
                await EnsureDefaultAsync(userId, address, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return address;
        }

        public async Task<bool> SetDefaultAsync(string userId, int id, CancellationToken cancellationToken = default)
        {
            var address = await GetAsync(userId, id, cancellationToken);
            if (address == null)
            {
                return false;
            }

            await SetDefaultInternalAsync(userId, address, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<ShippingAddressDeleteResult> DeleteAsync(string userId, int id, CancellationToken cancellationToken = default)
        {
            var address = await GetAsync(userId, id, cancellationToken);
            if (address == null)
            {
                return ShippingAddressDeleteResult.NotFound;
            }

            var used = await FindAddressesUsedInActiveOrdersAsync(userId, new[] { address }, cancellationToken);
            if (used.Contains(address.Id))
            {
                return ShippingAddressDeleteResult.BlockedByActiveOrder;
            }

            _dbContext.ShippingAddresses.Remove(address);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await EnsureDefaultExistsAsync(userId, cancellationToken);
            return ShippingAddressDeleteResult.Deleted;
        }

        public async Task<HashSet<int>> FindAddressesUsedInActiveOrdersAsync(string userId, IEnumerable<ShippingAddress> addresses, CancellationToken cancellationToken = default)
        {
            var list = addresses?.ToList() ?? new List<ShippingAddress>();
            if (string.IsNullOrWhiteSpace(userId) || list.Count == 0)
            {
                return new HashSet<int>();
            }

            var orders = await _dbContext.Orders.AsNoTracking()
                .Where(o => o.BuyerId == userId)
                .Select(o => new { o.DeliveryAddressJson, o.Status, o.SavedAddressKey })
                .ToListAsync(cancellationToken);

            var usedIds = new HashSet<int>();
            foreach (var order in orders)
            {
                var normalizedStatus = OrderStatuses.Normalize(order.Status);
                if (!ActiveStatuses.Contains(normalizedStatus))
                {
                    continue;
                }

                if (int.TryParse(order.SavedAddressKey, out var savedId) && list.Any(a => a.Id == savedId))
                {
                    usedIds.Add(savedId);
                }

                var orderAddress = Deserialize(order.DeliveryAddressJson);
                foreach (var saved in list)
                {
                    if (!usedIds.Contains(saved.Id) && AddressesEqual(orderAddress, ToDeliveryAddress(saved)))
                    {
                        usedIds.Add(saved.Id);
                    }
                }
            }

            return usedIds;
        }

        public DeliveryAddress ToDeliveryAddress(ShippingAddress address)
        {
            return new DeliveryAddress(
                address.Recipient,
                address.Line1,
                address.Line2,
                address.City,
                address.State,
                address.PostalCode,
                address.Country,
                address.Phone);
        }

        private async Task EnsureDefaultAsync(string userId, ShippingAddress address, CancellationToken cancellationToken)
        {
            var hasDefault = await _dbContext.ShippingAddresses.AsNoTracking()
                .AnyAsync(a => a.UserId == userId && a.IsDefault && a.Id != address.Id, cancellationToken);
            if (!hasDefault && !address.IsDefault)
            {
                await SetDefaultInternalAsync(userId, address, cancellationToken);
            }
        }

        private async Task EnsureDefaultExistsAsync(string userId, CancellationToken cancellationToken)
        {
            var hasDefault = await _dbContext.ShippingAddresses.AsNoTracking()
                .AnyAsync(a => a.UserId == userId && a.IsDefault, cancellationToken);
            if (hasDefault)
            {
                return;
            }

            var next = await _dbContext.ShippingAddresses
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.UpdatedOn)
                .FirstOrDefaultAsync(cancellationToken);

            if (next != null)
            {
                await SetDefaultInternalAsync(userId, next, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task SetDefaultInternalAsync(string userId, ShippingAddress address, CancellationToken cancellationToken)
        {
            var currentDefaults = await _dbContext.ShippingAddresses
                .Where(a => a.UserId == userId && a.Id != address.Id && a.IsDefault)
                .ToListAsync(cancellationToken);
            foreach (var existing in currentDefaults)
            {
                existing.IsDefault = false;
                existing.UpdatedOn = DateTimeOffset.UtcNow;
            }

            address.IsDefault = true;
            address.UpdatedOn = DateTimeOffset.UtcNow;
        }

        private static DeliveryAddress Normalize(AddressForm form)
        {
            return new DeliveryAddress(
                (form.Recipient ?? string.Empty).Trim(),
                (form.Line1 ?? string.Empty).Trim(),
                string.IsNullOrWhiteSpace(form.Line2) ? null : form.Line2.Trim(),
                (form.City ?? string.Empty).Trim(),
                (form.State ?? string.Empty).Trim(),
                (form.PostalCode ?? string.Empty).Trim(),
                (form.Country ?? string.Empty).Trim(),
                (form.Phone ?? string.Empty).Trim());
        }

        private void Apply(DeliveryAddress normalized, ShippingAddress target)
        {
            target.Recipient = normalized.Recipient;
            target.Line1 = normalized.Line1;
            target.Line2 = normalized.Line2;
            target.City = normalized.City;
            target.State = normalized.State;
            target.PostalCode = normalized.PostalCode;
            target.Country = normalized.Country ?? string.Empty;
            target.Phone = normalized.Phone ?? string.Empty;
        }

        private void ValidateAddress(AddressForm input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (string.IsNullOrWhiteSpace(input.Recipient)
                || string.IsNullOrWhiteSpace(input.Line1)
                || string.IsNullOrWhiteSpace(input.City)
                || string.IsNullOrWhiteSpace(input.PostalCode)
                || string.IsNullOrWhiteSpace(input.Country)
                || string.IsNullOrWhiteSpace(input.Phone))
            {
                throw new ValidationException("All required address fields must be provided.");
            }

            var trimmedCountry = (input.Country ?? string.Empty).Trim();
            if (!IsCountrySupported(trimmedCountry))
            {
                throw new ValidationException("The selected country is not supported for shipping.");
            }
        }

        private DeliveryAddress Deserialize(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new DeliveryAddress(string.Empty, string.Empty, null, string.Empty, string.Empty, string.Empty, string.Empty, null);
            }

            try
            {
                var address = JsonSerializer.Deserialize<DeliveryAddress>(payload, _serializerOptions);
                return address ?? new DeliveryAddress(string.Empty, string.Empty, null, string.Empty, string.Empty, string.Empty, string.Empty, null);
            }
            catch
            {
                return new DeliveryAddress(string.Empty, string.Empty, null, string.Empty, string.Empty, string.Empty, string.Empty, null);
            }
        }

        private static bool AddressesEqual(DeliveryAddress first, DeliveryAddress second)
        {
            return string.Equals(first.Recipient?.Trim() ?? string.Empty, second.Recipient?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.Line1?.Trim() ?? string.Empty, second.Line1?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.Line2?.Trim() ?? string.Empty, second.Line2?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.City?.Trim() ?? string.Empty, second.City?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.State?.Trim() ?? string.Empty, second.State?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.PostalCode?.Trim() ?? string.Empty, second.PostalCode?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.Country?.Trim() ?? string.Empty, second.Country?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(first.Phone?.Trim() ?? string.Empty, second.Phone?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
