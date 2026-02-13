using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public static class AdminUserStatuses
    {
        public const string Active = "Active";
        public const string Blocked = "Blocked";
        public const string PendingVerification = "Pending verification";

        public static readonly string[] All = [Active, Blocked, PendingVerification];

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return string.Empty;
            }

            if (status.Equals(Active, StringComparison.OrdinalIgnoreCase))
            {
                return Active;
            }

            if (status.Equals(Blocked, StringComparison.OrdinalIgnoreCase))
            {
                return Blocked;
            }

            if (status.Equals(PendingVerification, StringComparison.OrdinalIgnoreCase))
            {
                return PendingVerification;
            }

            return string.Empty;
        }

        public static List<string> NormalizeMany(IEnumerable<string> statuses) =>
            statuses.Select(Normalize)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    public record AdminUserListFilters
    {
        public string? Query { get; init; }

        public string? Role { get; init; }

        public List<string> Statuses { get; init; } = new();
    }

    public record AdminUserListItem(
        string Id,
        string Email,
        string FullName,
        string AccountType,
        string Status,
        DateTimeOffset? CreatedOn,
        DateTimeOffset? LastLoginOn,
        string? LastLoginIp);

    public record AdminUserLoginActivity(
        DateTimeOffset OccurredOn,
        string EventType,
        bool IsSuccess,
        bool IsUnusual,
        string? IpAddress,
        string? UserAgent);

    public record AdminUserAuditEntry(
        string Action,
        string Actor,
        DateTimeOffset OccurredOn,
        string? Reason);

    public record AdminUserDetail(
        string Id,
        string Email,
        string FullName,
        string AccountType,
        string Status,
        string AccountStatus,
        bool EmailConfirmed,
        string OnboardingStatus,
        string KycStatus,
        DateTimeOffset? CreatedOn,
        DateTimeOffset? LastLoginOn,
        string? LastLoginIp,
        DateTimeOffset? LockoutEnd,
        bool LockoutEnabled,
        IReadOnlyList<AdminUserLoginActivity> RecentLogins,
        DateTimeOffset? BlockedOn,
        string? BlockedBy,
        string? BlockReason,
        IReadOnlyList<AdminUserAuditEntry> AuditTrail);

    public class AdminUserService
    {
        private const int DefaultRecentLoginCount = 10;
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;

        public AdminUserService(ApplicationDbContext dbContext, TimeProvider timeProvider)
        {
            _dbContext = dbContext;
            _timeProvider = timeProvider;
        }

        public async Task<PagedResult<AdminUserListItem>> GetUsersAsync(
            AdminUserListFilters filters,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var now = _timeProvider.GetUtcNow();
            var normalizedRole = NormalizeRole(filters.Role);
            var normalizedStatuses = AdminUserStatuses.NormalizeMany(filters.Statuses);

            var query = _dbContext.Users.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filters.Query))
            {
                var term = filters.Query.Trim();
                query = query.Where(u =>
                    (u.Email != null && u.Email.Contains(term)) ||
                    u.FullName.Contains(term) ||
                    u.Id.Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(normalizedRole))
            {
                query = query.Where(u => u.AccountType == normalizedRole);
            }

            if (normalizedStatuses.Count > 0)
            {
                var wantsBlocked = normalizedStatuses.Contains(AdminUserStatuses.Blocked);
                var wantsPending = normalizedStatuses.Contains(AdminUserStatuses.PendingVerification);
                var wantsActive = normalizedStatuses.Contains(AdminUserStatuses.Active);

                query = query.Where(u =>
                    (wantsBlocked && u.LockoutEnabled && u.LockoutEnd.HasValue && u.LockoutEnd.Value >= now) ||
                    (wantsPending && (u.AccountStatus == AccountStatuses.Unverified ||
                                      !u.EmailConfirmed ||
                                      u.OnboardingStatus == OnboardingStatuses.PendingVerification)) ||
                    (wantsActive &&
                        (!u.LockoutEnabled || !u.LockoutEnd.HasValue || u.LockoutEnd.Value < now) &&
                        u.EmailConfirmed &&
                        u.AccountStatus != AccountStatuses.Unverified &&
                        u.OnboardingStatus != OnboardingStatuses.PendingVerification));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            pageNumber = Math.Max(1, pageNumber);

            var items = await query
                .OrderByDescending(u => u.TermsAcceptedOn ?? u.EmailVerifiedOn ?? u.OnboardingStartedOn ?? now)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new AdminUserListItem(
                    u.Id,
                    u.Email ?? string.Empty,
                    u.FullName,
                    u.AccountType,
                    DeriveStatus(u, now),
                    u.TermsAcceptedOn ?? u.EmailVerifiedOn ?? u.OnboardingStartedOn,
                    u.LastLoginOn,
                    u.LastLoginIp))
                .ToListAsync(cancellationToken);

            return new PagedResult<AdminUserListItem>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        public async Task<AdminUserDetail?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
        {
            var now = _timeProvider.GetUtcNow();
            var user = await _dbContext.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user == null)
            {
                return null;
            }

            var recentLogins = await _dbContext.LoginAudits.AsNoTracking()
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.OccurredOn)
                .Take(DefaultRecentLoginCount)
                .Select(l => new AdminUserLoginActivity(
                    l.OccurredOn,
                    l.EventType,
                    l.IsSuccess,
                    l.IsUnusual,
                    l.IpAddress,
                    l.UserAgent))
                .ToListAsync(cancellationToken);

            var createdOn = user.TermsAcceptedOn ?? user.EmailVerifiedOn ?? user.OnboardingStartedOn;

            var audits = await _dbContext.UserAdminAudits.AsNoTracking()
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.CreatedOn)
                .Take(20)
                .Select(a => new AdminUserAuditEntry(
                    a.Action,
                    string.IsNullOrWhiteSpace(a.ActorName) ? "System" : a.ActorName!,
                    a.CreatedOn,
                    a.Reason))
                .ToListAsync(cancellationToken);

            return new AdminUserDetail(
                user.Id,
                user.Email ?? string.Empty,
                user.FullName,
                user.AccountType,
                DeriveStatus(user, now),
                user.AccountStatus,
                user.EmailConfirmed,
                user.OnboardingStatus,
                user.KycStatus,
                createdOn,
                user.LastLoginOn,
                user.LastLoginIp,
                user.LockoutEnd,
                user.LockoutEnabled,
                recentLogins,
                user.BlockedOn,
                user.BlockedByName,
                user.BlockReason,
                audits);
        }

        private static string NormalizeRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return string.Empty;
            }

            return AccountTypes.Allowed.FirstOrDefault(a => a.Equals(role, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }

        private static string DeriveStatus(ApplicationUser user, DateTimeOffset now)
        {
            if (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value >= now)
            {
                return AdminUserStatuses.Blocked;
            }

            if (!user.EmailConfirmed ||
                string.Equals(user.AccountStatus, AccountStatuses.Unverified, StringComparison.OrdinalIgnoreCase) ||
                user.OnboardingStatus == OnboardingStatuses.PendingVerification)
            {
                return AdminUserStatuses.PendingVerification;
            }

            return AdminUserStatuses.Active;
        }
    }
}
