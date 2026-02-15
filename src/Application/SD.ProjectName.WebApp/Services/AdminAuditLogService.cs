using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public class AuditLogOptions
    {
        public const string SectionName = "AuditLog";

        [Range(30, 3650)]
        public int RetentionDays { get; set; } = 730;
    }

    public record AdminAuditLogFilters
    {
        public DateTimeOffset? From { get; init; }

        public DateTimeOffset? To { get; init; }

        public string? Actor { get; init; }

        public string? EntityType { get; init; }

        public string? ActionType { get; init; }

        public string? ResourceId { get; init; }
    }

    public record AdminAuditLogEntry(
        DateTimeOffset Timestamp,
        string EntityType,
        string ActionType,
        string? ResourceId,
        string ActorName,
        string? ActorId,
        string? Details);

    public class AdminAuditLogService
    {
        private static readonly HashSet<string> TrackedEntityTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "User",
            "Product",
            "ProductPhoto",
            "ProductReview",
            "CommissionRule",
            "VatRule"
        };

        private readonly ApplicationDbContext _applicationDbContext;
        private readonly ProductDbContext _productDbContext;
        private readonly AuditLogOptions _options;
        private readonly TimeProvider _timeProvider;

        public AdminAuditLogService(
            ApplicationDbContext applicationDbContext,
            ProductDbContext productDbContext,
            AuditLogOptions options,
            TimeProvider timeProvider)
        {
            _applicationDbContext = applicationDbContext;
            _productDbContext = productDbContext;
            _options = options;
            _timeProvider = timeProvider;
        }

        public IReadOnlyCollection<string> SupportedEntityTypes => TrackedEntityTypes;

        public async Task<PagedResult<AdminAuditLogEntry>> GetAsync(
            AdminAuditLogFilters filters,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var normalizedPage = Math.Max(1, pageNumber);
            var normalizedPageSize = Math.Clamp(pageSize, 1, 200);
            var retentionCutoff = _options.RetentionDays > 0
                ? _timeProvider.GetUtcNow().AddDays(-_options.RetentionDays)
                : (DateTimeOffset?)null;

            var effectiveFrom = filters.From ?? retentionCutoff;
            var effectiveTo = filters.To;
            var entries = new List<AdminAuditLogEntry>();

            if (IsIncluded(filters.EntityType, "User"))
            {
                var query = _applicationDbContext.UserAdminAudits.AsNoTracking();
                if (effectiveFrom.HasValue)
                {
                    query = query.Where(a => a.CreatedOn >= effectiveFrom.Value);
                }

                if (effectiveTo.HasValue)
                {
                    query = query.Where(a => a.CreatedOn <= effectiveTo.Value);
                }

                if (!string.IsNullOrWhiteSpace(filters.ResourceId))
                {
                    query = query.Where(a => a.UserId == filters.ResourceId);
                }

                var userEntries = await query
                    .Select(a => new AdminAuditLogEntry(
                        a.CreatedOn,
                        "User",
                        a.Action,
                        a.UserId,
                        string.IsNullOrWhiteSpace(a.ActorName) ? "System" : a.ActorName!,
                        a.ActorUserId,
                        a.Reason))
                    .ToListAsync(cancellationToken);

                entries.AddRange(userEntries);
            }

            if (IsIncluded(filters.EntityType, "Product"))
            {
                var query = _productDbContext.ProductModerationAudits.AsNoTracking();
                if (effectiveFrom.HasValue)
                {
                    query = query.Where(a => a.CreatedOn >= effectiveFrom.Value);
                }

                if (effectiveTo.HasValue)
                {
                    query = query.Where(a => a.CreatedOn <= effectiveTo.Value);
                }

                if (TryParseInt(filters.ResourceId, out var productId))
                {
                    query = query.Where(a => a.ProductId == productId);
                }

                var productEntries = await query
                    .Select(a => new AdminAuditLogEntry(
                        a.CreatedOn,
                        "Product",
                        a.Action,
                        a.ProductId.ToString(),
                        string.IsNullOrWhiteSpace(a.Actor) ? "System" : a.Actor!,
                        null,
                        BuildStatusDetail(a.FromStatus, a.ToStatus, a.Reason)))
                    .ToListAsync(cancellationToken);

                entries.AddRange(productEntries);
            }

            if (IsIncluded(filters.EntityType, "ProductPhoto"))
            {
                var query = _productDbContext.ProductPhotoModerationAudits.AsNoTracking();
                if (effectiveFrom.HasValue)
                {
                    query = query.Where(a => a.CreatedOn >= effectiveFrom.Value);
                }

                if (effectiveTo.HasValue)
                {
                    query = query.Where(a => a.CreatedOn <= effectiveTo.Value);
                }

                if (TryParseInt(filters.ResourceId, out var photoId))
                {
                    query = query.Where(a => a.PhotoId == photoId);
                }

                var photoEntries = await query
                    .Select(a => new AdminAuditLogEntry(
                        a.CreatedOn,
                        "ProductPhoto",
                        a.Action,
                        a.PhotoId.ToString(),
                        string.IsNullOrWhiteSpace(a.Actor) ? "System" : a.Actor!,
                        null,
                        BuildStatusDetail(a.FromStatus, a.ToStatus, a.Reason)))
                    .ToListAsync(cancellationToken);

                entries.AddRange(photoEntries);
            }

            if (IsIncluded(filters.EntityType, "ProductReview"))
            {
                var query = _applicationDbContext.ProductReviewAudits.AsNoTracking();
                if (effectiveFrom.HasValue)
                {
                    query = query.Where(a => a.CreatedOn >= effectiveFrom.Value);
                }

                if (effectiveTo.HasValue)
                {
                    query = query.Where(a => a.CreatedOn <= effectiveTo.Value);
                }

                if (TryParseInt(filters.ResourceId, out var reviewId))
                {
                    query = query.Where(a => a.ReviewId == reviewId);
                }

                var reviewEntries = await query
                    .Select(a => new AdminAuditLogEntry(
                        a.CreatedOn,
                        "ProductReview",
                        a.Action,
                        a.ReviewId.ToString(),
                        string.IsNullOrWhiteSpace(a.Actor) ? "System" : a.Actor!,
                        null,
                        BuildStatusDetail(a.FromStatus, a.ToStatus, a.Reason)))
                    .ToListAsync(cancellationToken);

                entries.AddRange(reviewEntries);
            }

            if (IsIncluded(filters.EntityType, "CommissionRule"))
            {
                var query = _applicationDbContext.CommissionRuleAudits.AsNoTracking();
                if (effectiveFrom.HasValue)
                {
                    query = query.Where(a => a.ChangedOn >= effectiveFrom.Value);
                }

                if (effectiveTo.HasValue)
                {
                    query = query.Where(a => a.ChangedOn <= effectiveTo.Value);
                }

                if (TryParseInt(filters.ResourceId, out var ruleId))
                {
                    query = query.Where(a => a.RuleId == ruleId);
                }

                var commissionEntries = await query
                    .Select(a => new AdminAuditLogEntry(
                        a.ChangedOn,
                        "CommissionRule",
                        a.Action,
                        a.RuleId.ToString(),
                        string.IsNullOrWhiteSpace(a.ChangedByName) ? "System" : a.ChangedByName!,
                        a.ChangedBy,
                        "Commission rule change captured."))
                    .ToListAsync(cancellationToken);

                entries.AddRange(commissionEntries);
            }

            if (IsIncluded(filters.EntityType, "VatRule"))
            {
                var query = _applicationDbContext.VatRuleAudits.AsNoTracking();
                if (effectiveFrom.HasValue)
                {
                    query = query.Where(a => a.ChangedOn >= effectiveFrom.Value);
                }

                if (effectiveTo.HasValue)
                {
                    query = query.Where(a => a.ChangedOn <= effectiveTo.Value);
                }

                if (TryParseInt(filters.ResourceId, out var vatRuleId))
                {
                    query = query.Where(a => a.RuleId == vatRuleId);
                }

                var vatEntries = await query
                    .Select(a => new AdminAuditLogEntry(
                        a.ChangedOn,
                        "VatRule",
                        a.Action,
                        a.RuleId.ToString(),
                        string.IsNullOrWhiteSpace(a.ChangedByName) ? "System" : a.ChangedByName!,
                        a.ChangedBy,
                        "VAT rule change captured."))
                    .ToListAsync(cancellationToken);

                entries.AddRange(vatEntries);
            }

            var filtered = ApplyPostFilters(entries, filters);
            var ordered = filtered.OrderByDescending(e => e.Timestamp).ToList();
            var totalCount = ordered.Count;
            var pagedItems = ordered
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();

            return new PagedResult<AdminAuditLogEntry>
            {
                Items = pagedItems,
                PageNumber = normalizedPage,
                PageSize = normalizedPageSize,
                TotalCount = totalCount
            };
        }

        private static IEnumerable<AdminAuditLogEntry> ApplyPostFilters(
            IEnumerable<AdminAuditLogEntry> entries,
            AdminAuditLogFilters filters)
        {
            var result = entries;

            if (!string.IsNullOrWhiteSpace(filters.Actor))
            {
                result = result.Where(e => e.ActorName.Contains(filters.Actor, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(filters.ActionType))
            {
                result = result.Where(e => e.ActionType.Contains(filters.ActionType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(filters.EntityType))
            {
                result = result.Where(e => string.Equals(e.EntityType, filters.EntityType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(filters.ResourceId))
            {
                result = result.Where(e => string.Equals(e.ResourceId, filters.ResourceId, StringComparison.OrdinalIgnoreCase));
            }

            return result;
        }

        private static bool IsIncluded(string? filterEntity, string entityType)
        {
            return string.IsNullOrWhiteSpace(filterEntity) ||
                   string.Equals(filterEntity, entityType, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseInt(string? value, out int parsed)
        {
            return int.TryParse(value, out parsed);
        }

        private static string BuildStatusDetail(string? fromStatus, string? toStatus, string? reason)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(fromStatus) || !string.IsNullOrWhiteSpace(toStatus))
            {
                parts.Add($"Status {fromStatus ?? "?"} -> {toStatus ?? "?"}");
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                parts.Add(reason.Trim());
            }

            return parts.Count == 0 ? string.Empty : string.Join(" | ", parts);
        }
    }
}
