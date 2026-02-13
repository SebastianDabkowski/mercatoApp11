using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public class VatRule
    {
        public int Id { get; set; }

        [MaxLength(120)]
        public string Country { get; set; } = string.Empty;

        [Range(0, 1)]
        public decimal Rate { get; set; }

        [MaxLength(512)]
        public string? Categories { get; set; }

        public DateTimeOffset EffectiveFrom { get; set; }

        public DateTimeOffset? EffectiveTo { get; set; }

        [MaxLength(450)]
        public string? CreatedBy { get; set; }

        [MaxLength(256)]
        public string? CreatedByName { get; set; }

        public DateTimeOffset CreatedOn { get; set; }

        [MaxLength(450)]
        public string? UpdatedBy { get; set; }

        [MaxLength(256)]
        public string? UpdatedByName { get; set; }

        public DateTimeOffset? UpdatedOn { get; set; }
    }

    public class VatRuleAudit
    {
        public int Id { get; set; }

        public int RuleId { get; set; }

        [MaxLength(32)]
        public string Action { get; set; } = "Updated";

        public string SnapshotJson { get; set; } = string.Empty;

        [MaxLength(450)]
        public string? ChangedBy { get; set; }

        [MaxLength(256)]
        public string? ChangedByName { get; set; }

        public DateTimeOffset ChangedOn { get; set; }
    }

    public record VatRuleInput
    {
        public int? Id { get; init; }

        [MaxLength(120)]
        public string Country { get; init; } = string.Empty;

        public decimal Rate { get; init; }

        public IEnumerable<string>? Categories { get; init; }

        public DateTimeOffset EffectiveFrom { get; init; }

        public DateTimeOffset? EffectiveTo { get; init; }
    }

    public record VatRuleResolution(int? RuleId, decimal Rate, string Country, IReadOnlyList<string> Categories);

    public record VatRuleOperationResult(bool Success, VatRule? Rule, List<string> Errors)
    {
        public static VatRuleOperationResult Failed(params string[] errors) => new(false, null, errors.ToList());

        public static VatRuleOperationResult Succeeded(VatRule rule) => new(true, rule, new List<string>());
    }

    public interface IVatRuleResolver
    {
        VatRuleResolution Resolve(string country, string? category, DateTimeOffset asOf);
    }

    public class VatRuleService : IVatRuleResolver
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly InvoiceOptions _invoiceOptions;
        private readonly ILogger<VatRuleService>? _logger;
        private readonly object _cacheLock = new();
        private List<VatRule> _cachedRules = new();

        public VatRuleService(ApplicationDbContext dbContext, InvoiceOptions invoiceOptions, ILogger<VatRuleService>? logger = null)
        {
            _dbContext = dbContext;
            _invoiceOptions = invoiceOptions;
            _logger = logger;
        }

        public async Task<List<VatRule>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var rules = await _dbContext.VatRules.AsNoTracking()
                .OrderByDescending(r => r.EffectiveFrom)
                .ThenBy(r => r.Country)
                .ToListAsync(cancellationToken);

            lock (_cacheLock)
            {
                _cachedRules = rules;
            }

            return rules;
        }

        public async Task<VatRule?> FindAsync(int id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.VatRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        }

        public async Task<List<VatRuleAudit>> GetHistoryAsync(int? ruleId = null, CancellationToken cancellationToken = default)
        {
            var query = _dbContext.VatRuleAudits.AsNoTracking();
            if (ruleId.HasValue)
            {
                query = query.Where(a => a.RuleId == ruleId.Value);
            }

            return await query
                .OrderByDescending(a => a.ChangedOn)
                .Take(200)
                .ToListAsync(cancellationToken);
        }

        public VatRuleResolution Resolve(string country, string? category, DateTimeOffset asOf)
        {
            var normalizedCountry = NormalizeCountry(country);
            var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
            try
            {
                var rules = GetCachedRules();
                var applicable = rules.Where(r =>
                        string.Equals(r.Country, normalizedCountry, StringComparison.OrdinalIgnoreCase) &&
                        r.EffectiveFrom <= asOf &&
                        (r.EffectiveTo == null || r.EffectiveTo.Value >= asOf) &&
                        MatchesCategory(normalizedCategory, SplitCategories(r.Categories)))
                    .OrderByDescending(r => Priority(r))
                    .ThenByDescending(r => r.EffectiveFrom)
                    .ToList();

                var match = applicable.FirstOrDefault();
                if (match != null)
                {
                    return new VatRuleResolution(
                        match.Id,
                        ClampRate(match.Rate),
                        match.Country,
                        SplitCategories(match.Categories));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to resolve VAT rule, using default rate.");
            }

            return new VatRuleResolution(null, ClampRate(_invoiceOptions.TaxRate), normalizedCountry, Array.Empty<string>());
        }

        public async Task<VatRuleOperationResult> SaveAsync(
            VatRuleInput input,
            string actorId,
            string? actorName,
            CancellationToken cancellationToken = default)
        {
            var normalizedCountry = NormalizeCountry(input.Country);
            var normalizedCategories = NormalizeCategories(input.Categories);
            var errors = ValidateInput(normalizedCountry, input.Rate, input.EffectiveFrom, input.EffectiveTo);
            if (errors.Count > 0)
            {
                return VatRuleOperationResult.Failed(errors.ToArray());
            }

            var conflicts = await FindConflictsAsync(input.Id, normalizedCountry, normalizedCategories, input.EffectiveFrom, input.EffectiveTo, cancellationToken);
            if (conflicts.Count > 0)
            {
                errors.AddRange(conflicts);
                return VatRuleOperationResult.Failed(errors.ToArray());
            }

            VatRule? rule = null;
            if (input.Id.HasValue)
            {
                rule = await _dbContext.VatRules.FirstOrDefaultAsync(r => r.Id == input.Id.Value, cancellationToken);
            }

            if (rule == null)
            {
                rule = new VatRule
                {
                    CreatedBy = actorId,
                    CreatedByName = actorName,
                    CreatedOn = DateTimeOffset.UtcNow
                };
                _dbContext.VatRules.Add(rule);
            }

            rule.Country = normalizedCountry;
            rule.Rate = ClampRate(input.Rate);
            rule.Categories = JoinCategories(normalizedCategories);
            rule.EffectiveFrom = input.EffectiveFrom;
            rule.EffectiveTo = input.EffectiveTo;
            rule.UpdatedBy = actorId;
            rule.UpdatedByName = actorName;
            rule.UpdatedOn = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await RecordAuditAsync(rule, input.Id.HasValue ? "Updated" : "Created", actorId, actorName, cancellationToken);
            InvalidateCache();
            return VatRuleOperationResult.Succeeded(rule);
        }

        private async Task<List<string>> FindConflictsAsync(int? id, string country, List<string> categories, DateTimeOffset start, DateTimeOffset? end, CancellationToken cancellationToken)
        {
            var overlapping = await _dbContext.VatRules.AsNoTracking()
                .Where(r => (!id.HasValue || r.Id != id.Value)
                            && string.Equals(r.Country, country, StringComparison.OrdinalIgnoreCase)
                            && RangesOverlap(r.EffectiveFrom, r.EffectiveTo, start, end)
                            && CategoriesOverlap(categories, SplitCategories(r.Categories)))
                .OrderBy(r => r.EffectiveFrom)
                .ToListAsync(cancellationToken);

            if (overlapping.Count == 0)
            {
                return new List<string>();
            }

            var errors = new List<string>();
            foreach (var rule in overlapping)
            {
                var effectiveTo = rule.EffectiveTo.HasValue ? rule.EffectiveTo.Value.ToString("u") : "open-ended";
                errors.Add($"Conflicts with rule effective {rule.EffectiveFrom:u} - {effectiveTo} for {rule.Country}.");
            }

            return errors;
        }

        private List<VatRule> GetCachedRules()
        {
            lock (_cacheLock)
            {
                if (_cachedRules.Count == 0)
                {
                    _cachedRules = _dbContext.VatRules.AsNoTracking().ToList();
                }

                return _cachedRules.ToList();
            }
        }

        private void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedRules = new List<VatRule>();
            }
        }

        private static string NormalizeCountry(string country) =>
            string.IsNullOrWhiteSpace(country) ? string.Empty : country.Trim().ToUpperInvariant();

        private static List<string> SplitCategories(string? categories)
        {
            if (string.IsNullOrWhiteSpace(categories))
            {
                return new List<string>();
            }

            return categories
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string? JoinCategories(IEnumerable<string> categories) =>
            categories.Any() ? string.Join(",", categories) : null;

        private static List<string> NormalizeCategories(IEnumerable<string>? categories) =>
            categories == null
                ? new List<string>()
                : categories
                    .Select(c => c?.Trim())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList()!;

        private static bool MatchesCategory(string? target, List<string> ruleCategories)
        {
            if (ruleCategories.Count == 0)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(target) && ruleCategories.Contains(target, StringComparer.OrdinalIgnoreCase);
        }

        private static bool CategoriesOverlap(List<string> lhs, List<string> rhs)
        {
            if (lhs.Count == 0 || rhs.Count == 0)
            {
                return true;
            }

            return lhs.Any(c => rhs.Contains(c, StringComparer.OrdinalIgnoreCase));
        }

        private static bool RangesOverlap(DateTimeOffset start1, DateTimeOffset? end1, DateTimeOffset start2, DateTimeOffset? end2)
        {
            var effectiveEnd1 = end1 ?? DateTimeOffset.MaxValue;
            var effectiveEnd2 = end2 ?? DateTimeOffset.MaxValue;
            return start1 <= effectiveEnd2 && start2 <= effectiveEnd1;
        }

        private async Task RecordAuditAsync(VatRule rule, string action, string actorId, string? actorName, CancellationToken cancellationToken)
        {
            var audit = new VatRuleAudit
            {
                RuleId = rule.Id,
                Action = action,
                SnapshotJson = JsonSerializer.Serialize(rule),
                ChangedBy = actorId,
                ChangedByName = actorName,
                ChangedOn = DateTimeOffset.UtcNow
            };

            _dbContext.VatRuleAudits.Add(audit);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private static List<string> ValidateInput(string country, decimal rate, DateTimeOffset start, DateTimeOffset? end)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(country))
            {
                errors.Add("Country is required.");
            }

            if (rate < 0 || rate > 1)
            {
                errors.Add("Rate must be between 0 and 1.");
            }

            if (end.HasValue && end.Value < start)
            {
                errors.Add("Effective end date must be after the start date.");
            }

            return errors;
        }

        private static int Priority(VatRule rule)
        {
            var score = 0;
            if (SplitCategories(rule.Categories).Count > 0)
            {
                score += 1;
            }

            return score;
        }

        private static decimal ClampRate(decimal rate)
        {
            if (rate < 0)
            {
                return 0;
            }

            if (rate > 1)
            {
                return 1;
            }

            return rate;
        }
    }
}
