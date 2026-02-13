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
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public class CommissionRule
    {
        public int Id { get; set; }

        [MaxLength(256)]
        public string Name { get; set; } = string.Empty;

        [Range(0, 1)]
        public decimal Rate { get; set; }

        [Range(0, double.MaxValue)]
        public decimal FixedFee { get; set; }

        [MaxLength(256)]
        public string? Category { get; set; }

        [MaxLength(32)]
        public string? SellerType { get; set; }

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

    public class CommissionRuleAudit
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

    public record CommissionRuleResolution(int? RuleId, decimal Rate, decimal FixedFee, string Key);

    public record CommissionRuleInput
    {
        public int? Id { get; init; }

        [MaxLength(256)]
        public string Name { get; init; } = string.Empty;

        public decimal Rate { get; init; }

        public decimal FixedFee { get; init; }

        [MaxLength(256)]
        public string? Category { get; init; }

        [MaxLength(32)]
        public string? SellerType { get; init; }

        public DateTimeOffset EffectiveFrom { get; init; }

        public DateTimeOffset? EffectiveTo { get; init; }
    }

    public record CommissionRuleOperationResult(bool Success, CommissionRule? Rule, List<string> Errors)
    {
        public static CommissionRuleOperationResult Failed(params string[] errors) => new(false, null, errors.ToList());
        public static CommissionRuleOperationResult Succeeded(CommissionRule rule) => new(true, rule, new List<string>());
    }

    public interface ICommissionRuleResolver
    {
        CommissionRuleResolution Resolve(string sellerId, string? category, string? sellerType, DateTimeOffset asOf);
    }

    public class CommissionRuleService : ICommissionRuleResolver
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly CartOptions _cartOptions;
        private readonly ILogger<CommissionRuleService>? _logger;
        private readonly object _cacheLock = new();
        private List<CommissionRule> _cachedRules = new();

        public CommissionRuleService(ApplicationDbContext dbContext, CartOptions cartOptions, ILogger<CommissionRuleService>? logger = null)
        {
            _dbContext = dbContext;
            _cartOptions = cartOptions;
            _logger = logger;
        }

        public async Task<List<CommissionRule>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var rules = await _dbContext.CommissionRules.AsNoTracking()
                .OrderByDescending(r => r.EffectiveFrom)
                .ThenBy(r => r.Name)
                .ToListAsync(cancellationToken);

            lock (_cacheLock)
            {
                _cachedRules = rules;
            }

            return rules;
        }

        public async Task<CommissionRule?> FindAsync(int id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.CommissionRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        }

        public async Task<CommissionRuleOperationResult> SaveAsync(
            CommissionRuleInput input,
            string actorId,
            string? actorName,
            CancellationToken cancellationToken = default)
        {
            var errors = ValidateInput(input);
            if (errors.Count > 0)
            {
                return CommissionRuleOperationResult.Failed(errors.ToArray());
            }

            var normalizedCategory = NormalizeCategory(input.Category);
            var normalizedSellerType = NormalizeSellerType(input.SellerType);

            var conflicts = await FindConflictsAsync(input.Id, normalizedCategory, normalizedSellerType, input.EffectiveFrom, input.EffectiveTo, cancellationToken);
            if (conflicts.Count > 0)
            {
                errors.AddRange(conflicts);
                return CommissionRuleOperationResult.Failed(errors.ToArray());
            }

            CommissionRule? rule = null;
            if (input.Id.HasValue)
            {
                rule = await _dbContext.CommissionRules.FirstOrDefaultAsync(r => r.Id == input.Id.Value, cancellationToken);
            }

            if (rule == null)
            {
                rule = new CommissionRule
                {
                    CreatedBy = actorId,
                    CreatedByName = actorName,
                    CreatedOn = DateTimeOffset.UtcNow
                };
                _dbContext.CommissionRules.Add(rule);
            }

            rule.Name = input.Name?.Trim() ?? string.Empty;
            rule.Rate = ClampRate(input.Rate);
            rule.FixedFee = Math.Max(0, input.FixedFee);
            rule.Category = normalizedCategory;
            rule.SellerType = normalizedSellerType;
            rule.EffectiveFrom = input.EffectiveFrom;
            rule.EffectiveTo = input.EffectiveTo;
            rule.UpdatedBy = actorId;
            rule.UpdatedByName = actorName;
            rule.UpdatedOn = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            await RecordAuditAsync(rule, input.Id.HasValue ? "Updated" : "Created", actorId, actorName, cancellationToken);
            InvalidateCache();
            return CommissionRuleOperationResult.Succeeded(rule);
        }

        public CommissionRuleResolution Resolve(string sellerId, string? category, string? sellerType, DateTimeOffset asOf)
        {
            try
            {
                var rules = GetCachedRules();
                var applicable = rules.Where(r =>
                        r.EffectiveFrom <= asOf &&
                        (r.EffectiveTo == null || r.EffectiveTo.Value >= asOf) &&
                        MatchesCategory(category, r.Category) &&
                        MatchesSellerType(sellerType, r.SellerType))
                    .OrderByDescending(r => Priority(r))
                    .ThenByDescending(r => r.EffectiveFrom)
                    .ToList();

                var match = applicable.FirstOrDefault();
                if (match != null)
                {
                    return new CommissionRuleResolution(match.Id, ClampRate(match.Rate), Math.Max(0, match.FixedFee), $"rule-{match.Id}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to resolve commission rule, falling back to defaults");
            }

            var normalizedSellerId = string.IsNullOrWhiteSpace(sellerId) ? null : sellerId.Trim();
            if (normalizedSellerId != null &&
                _cartOptions.SellerCommissionOverrides != null &&
                _cartOptions.SellerCommissionOverrides.TryGetValue(normalizedSellerId, out var sellerRate))
            {
                return new CommissionRuleResolution(null, ClampRate(sellerRate), Math.Max(0, _cartOptions.PlatformFixedFee), $"seller-{normalizedSellerId}");
            }

            if (!string.IsNullOrWhiteSpace(category)
                && _cartOptions.CategoryCommissionRates != null
                && _cartOptions.CategoryCommissionRates.TryGetValue(category!, out var categoryRate))
            {
                return new CommissionRuleResolution(null, ClampRate(categoryRate), Math.Max(0, _cartOptions.PlatformFixedFee), $"category-{category}".ToLowerInvariant());
            }

            return new CommissionRuleResolution(null, ClampRate(_cartOptions.PlatformCommissionRate), Math.Max(0, _cartOptions.PlatformFixedFee), "default");
        }

        private List<CommissionRule> GetCachedRules()
        {
            lock (_cacheLock)
            {
                if (_cachedRules.Count == 0)
                {
                    _cachedRules = _dbContext.CommissionRules.AsNoTracking().ToList();
                }

                return _cachedRules.ToList();
            }
        }

        private void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedRules = new List<CommissionRule>();
            }
        }

        private static string? NormalizeCategory(string? category) =>
            string.IsNullOrWhiteSpace(category) ? null : category.Trim();

        private static string? NormalizeSellerType(string? sellerType) =>
            SellerTypes.Allowed.FirstOrDefault(t => string.Equals(t, sellerType, StringComparison.OrdinalIgnoreCase));

        private static bool MatchesCategory(string? target, string? ruleCategory)
        {
            if (string.IsNullOrWhiteSpace(ruleCategory))
            {
                return true;
            }

            return string.Equals(ruleCategory, target, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesSellerType(string? target, string? ruleSellerType)
        {
            if (string.IsNullOrWhiteSpace(ruleSellerType))
            {
                return true;
            }

            return string.Equals(ruleSellerType, target, StringComparison.OrdinalIgnoreCase);
        }

        private static int Priority(CommissionRule rule)
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(rule.SellerType))
            {
                score += 2;
            }

            if (!string.IsNullOrWhiteSpace(rule.Category))
            {
                score += 1;
            }

            return score;
        }

        private static List<string> ValidateInput(CommissionRuleInput input)
        {
            var errors = new List<string>();
            if (input.Rate < 0 || input.Rate > 1)
            {
                errors.Add("Rate must be between 0 and 1.");
            }

            if (input.FixedFee < 0)
            {
                errors.Add("Fixed fee cannot be negative.");
            }

            if (input.EffectiveTo.HasValue && input.EffectiveTo.Value < input.EffectiveFrom)
            {
                errors.Add("Effective end date must be after the start date.");
            }

            if (!string.IsNullOrWhiteSpace(input.SellerType) && !SellerTypes.IsValid(input.SellerType))
            {
                errors.Add("Seller type is not supported.");
            }

            return errors;
        }

        private async Task<List<string>> FindConflictsAsync(int? id, string? category, string? sellerType, DateTimeOffset start, DateTimeOffset? end, CancellationToken cancellationToken)
        {
            var overlapping = await _dbContext.CommissionRules.AsNoTracking()
                .Where(r => (!id.HasValue || r.Id != id.Value)
                            && MatchesCategory(category, r.Category)
                            && MatchesSellerType(sellerType, r.SellerType)
                            && RangesOverlap(r.EffectiveFrom, r.EffectiveTo, start, end))
                .OrderBy(r => r.EffectiveFrom)
                .ToListAsync(cancellationToken);

            if (overlapping.Count == 0)
            {
                return new List<string>();
            }

            return overlapping
                .Select(r => $"Conflicts with rule '{r.Name}' effective {r.EffectiveFrom:u} - {(r.EffectiveTo?.ToString("u") ?? "open-ended")}.")
                .ToList();
        }

        private static bool RangesOverlap(DateTimeOffset start1, DateTimeOffset? end1, DateTimeOffset start2, DateTimeOffset? end2)
        {
            var effectiveEnd1 = end1 ?? DateTimeOffset.MaxValue;
            var effectiveEnd2 = end2 ?? DateTimeOffset.MaxValue;
            return start1 <= effectiveEnd2 && start2 <= effectiveEnd1;
        }

        private async Task RecordAuditAsync(CommissionRule rule, string action, string actorId, string? actorName, CancellationToken cancellationToken)
        {
            var audit = new CommissionRuleAudit
            {
                RuleId = rule.Id,
                Action = action,
                SnapshotJson = JsonSerializer.Serialize(rule),
                ChangedBy = actorId,
                ChangedByName = actorName,
                ChangedOn = DateTimeOffset.UtcNow
            };

            _dbContext.CommissionRuleAudits.Add(audit);
            await _dbContext.SaveChangesAsync(cancellationToken);
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
