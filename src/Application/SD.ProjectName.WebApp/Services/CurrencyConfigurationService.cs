using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public class CurrencySetting
    {
        public int Id { get; set; }

        [MaxLength(16)]
        public string Code { get; set; } = "USD";

        [MaxLength(128)]
        public string? Name { get; set; }

        public bool EnabledForDisplay { get; set; }

        public bool EnabledForTransactions { get; set; }

        public bool IsBase { get; set; }

        public decimal? LatestRate { get; set; }

        [MaxLength(128)]
        public string? RateSource { get; set; }

        public DateTimeOffset? RateUpdatedOn { get; set; }

        public DateTimeOffset CreatedOn { get; set; }

        public DateTimeOffset? UpdatedOn { get; set; }
    }

    public record CurrencySettingsInput(
        List<CurrencyUpdateInput> Currencies,
        string BaseCurrency,
        bool ConfirmBaseCurrencyChange,
        NewCurrencyInput? NewCurrency = null);

    public record CurrencyUpdateInput(int? Id, string Code, string? Name, bool EnabledForDisplay, bool EnabledForTransactions);

    public record NewCurrencyInput(string Code, string? Name, bool EnabledForDisplay, bool EnabledForTransactions);

    public record CurrencyOperationResult(bool Success, List<string> Errors)
    {
        public static CurrencyOperationResult Failed(params string[] errors) => new(false, errors.ToList());

        public static CurrencyOperationResult Succeeded() => new(true, new List<string>());
    }

    public class CurrencyConfigurationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly InvoiceOptions _invoiceOptions;
        private readonly ILogger<CurrencyConfigurationService>? _logger;
        private readonly object _cacheLock = new();
        private List<CurrencySetting> _cachedCurrencies = new();

        public CurrencyConfigurationService(
            ApplicationDbContext dbContext,
            InvoiceOptions invoiceOptions,
            ILogger<CurrencyConfigurationService>? logger = null)
        {
            _dbContext = dbContext;
            _invoiceOptions = invoiceOptions;
            _logger = logger;
        }

        public async Task<List<CurrencySetting>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var cached = GetCachedCurrencies();
            if (cached.Count > 0)
            {
                return cached;
            }

            var currencies = await _dbContext.CurrencySettings.AsNoTracking()
                .OrderBy(c => c.Code)
                .ToListAsync(cancellationToken);

            if (currencies.Count == 0)
            {
                currencies = await SeedDefaultAsync(cancellationToken);
            }

            UpdateCache(currencies);
            return currencies;
        }

        public async Task<string> GetBaseCurrencyAsync(CancellationToken cancellationToken = default)
        {
            var cached = GetCachedCurrencies();
            var baseCurrency = cached.FirstOrDefault(c => c.IsBase)?.Code;
            if (!string.IsNullOrWhiteSpace(baseCurrency))
            {
                return baseCurrency;
            }

            var currencies = await GetAllAsync(cancellationToken);
            return currencies.FirstOrDefault(c => c.IsBase)?.Code ?? NormalizeCode(_invoiceOptions.Currency) ?? "USD";
        }

        public async Task<string> ResolveTransactionCurrencyAsync(string? requestedCurrency, CancellationToken cancellationToken = default)
        {
            var currencies = await GetAllAsync(cancellationToken);
            var normalized = NormalizeCode(requestedCurrency);
            var baseCurrency = currencies.FirstOrDefault(c => c.IsBase)?.Code ?? NormalizeCode(_invoiceOptions.Currency) ?? "USD";

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                var match = currencies.FirstOrDefault(c => string.Equals(c.Code, normalized, StringComparison.OrdinalIgnoreCase));
                if (match != null && match.EnabledForTransactions)
                {
                    return match.Code;
                }
            }

            return baseCurrency;
        }

        public async Task<CurrencyOperationResult> SaveAsync(
            CurrencySettingsInput input,
            CancellationToken cancellationToken = default)
        {
            var errors = new List<string>();
            var normalizedBase = NormalizeCode(input.BaseCurrency);
            if (string.IsNullOrWhiteSpace(normalizedBase))
            {
                errors.Add("Select a base currency.");
                return CurrencyOperationResult.Failed(errors.ToArray());
            }

            var existing = await _dbContext.CurrencySettings
                .OrderBy(c => c.Code)
                .ToListAsync(cancellationToken);

            if (existing.Count == 0)
            {
                existing = await SeedDefaultAsync(cancellationToken);
            }

            var normalizedCurrencies = new List<CurrencyUpdateInput>();
            foreach (var currency in input.Currencies ?? new List<CurrencyUpdateInput>())
            {
                var code = NormalizeCode(currency.Code);
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                normalizedCurrencies.Add(currency with { Code = code });
            }

            if (input.NewCurrency != null && !string.IsNullOrWhiteSpace(input.NewCurrency.Value.Code))
            {
                var newCode = NormalizeCode(input.NewCurrency.Value.Code);
                if (!string.IsNullOrWhiteSpace(newCode))
                {
                    normalizedCurrencies.Add(new CurrencyUpdateInput(
                        null,
                        newCode,
                        input.NewCurrency.Value.Name,
                        input.NewCurrency.Value.EnabledForDisplay,
                        input.NewCurrency.Value.EnabledForTransactions));
                }
            }

            var duplicates = normalizedCurrencies
                .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicates.Count > 0)
            {
                errors.Add($"Duplicate currency codes: {string.Join(", ", duplicates)}.");
                return CurrencyOperationResult.Failed(errors.ToArray());
            }

            var targetBase = normalizedBase!;
            if (!normalizedCurrencies.Any(c => string.Equals(c.Code, targetBase, StringComparison.OrdinalIgnoreCase))
                && !existing.Any(c => string.Equals(c.Code, targetBase, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("Base currency must be part of the configured list.");
            }

            var previousBase = existing.FirstOrDefault(c => c.IsBase)?.Code ?? NormalizeCode(_invoiceOptions.Currency) ?? "USD";
            var baseChanged = !string.Equals(previousBase, targetBase, StringComparison.OrdinalIgnoreCase);
            if (baseChanged && !input.ConfirmBaseCurrencyChange)
            {
                errors.Add("Changing base currency requires confirmation.");
            }

            if (errors.Count > 0)
            {
                return CurrencyOperationResult.Failed(errors.ToArray());
            }

            foreach (var currency in normalizedCurrencies)
            {
                var entity = existing.FirstOrDefault(c =>
                    (currency.Id.HasValue && c.Id == currency.Id.Value) ||
                    string.Equals(c.Code, currency.Code, StringComparison.OrdinalIgnoreCase));

                if (entity == null)
                {
                    entity = new CurrencySetting
                    {
                        Code = currency.Code,
                        Name = string.IsNullOrWhiteSpace(currency.Name) ? currency.Code : currency.Name?.Trim(),
                        EnabledForDisplay = currency.EnabledForDisplay,
                        EnabledForTransactions = currency.EnabledForTransactions,
                        CreatedOn = DateTimeOffset.UtcNow
                    };
                    existing.Add(entity);
                    _dbContext.CurrencySettings.Add(entity);
                }
                else
                {
                    entity.Name = string.IsNullOrWhiteSpace(currency.Name) ? entity.Name ?? currency.Code : currency.Name?.Trim();
                    entity.EnabledForDisplay = currency.EnabledForDisplay;
                    entity.EnabledForTransactions = currency.EnabledForTransactions;
                    entity.UpdatedOn = DateTimeOffset.UtcNow;
                }

                entity.IsBase = string.Equals(entity.Code, targetBase, StringComparison.OrdinalIgnoreCase);
                if (entity.IsBase)
                {
                    entity.EnabledForDisplay = true;
                    entity.EnabledForTransactions = true;
                    entity.LatestRate ??= 1;
                }
            }

            foreach (var currency in existing)
            {
                if (!string.Equals(currency.Code, targetBase, StringComparison.OrdinalIgnoreCase))
                {
                    currency.IsBase = false;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            InvalidateCache();

            return CurrencyOperationResult.Succeeded();
        }

        public async Task<bool> TryRecordRateUpdateAsync(string code, decimal rate, string? source, DateTimeOffset? updatedOn = null, CancellationToken cancellationToken = default)
        {
            var normalized = NormalizeCode(code);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            var entity = await _dbContext.CurrencySettings.FirstOrDefaultAsync(
                c => c.Code == normalized,
                cancellationToken);

            if (entity == null)
            {
                return false;
            }

            entity.LatestRate = rate;
            entity.RateSource = string.IsNullOrWhiteSpace(source) ? entity.RateSource : source;
            entity.RateUpdatedOn = updatedOn ?? DateTimeOffset.UtcNow;
            entity.UpdatedOn = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            InvalidateCache();
            return true;
        }

        private async Task<List<CurrencySetting>> SeedDefaultAsync(CancellationToken cancellationToken)
        {
            var baseCurrency = NormalizeCode(_invoiceOptions.Currency) ?? "USD";
            var entity = new CurrencySetting
            {
                Code = baseCurrency,
                Name = baseCurrency,
                EnabledForDisplay = true,
                EnabledForTransactions = true,
                IsBase = true,
                LatestRate = 1,
                RateSource = "Configured default",
                RateUpdatedOn = DateTimeOffset.UtcNow,
                CreatedOn = DateTimeOffset.UtcNow
            };

            _dbContext.CurrencySettings.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new List<CurrencySetting> { entity };
        }

        private List<CurrencySetting> GetCachedCurrencies()
        {
            lock (_cacheLock)
            {
                return _cachedCurrencies.ToList();
            }
        }

        private void UpdateCache(List<CurrencySetting> currencies)
        {
            lock (_cacheLock)
            {
                _cachedCurrencies = currencies.ToList();
            }
        }

        private void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedCurrencies = new List<CurrencySetting>();
            }
        }

        private static string? NormalizeCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            return code.Trim().ToUpperInvariant();
        }
    }
}
