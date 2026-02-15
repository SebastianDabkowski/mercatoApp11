using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public record CriticalActionAuditEntry(
        string ActionType,
        string? ResourceType,
        string? ResourceId,
        string ActorName,
        string? ActorUserId,
        bool IsSuccess,
        string? Details);

    public class CriticalActionAuditService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;

        public CriticalActionAuditService(ApplicationDbContext dbContext, TimeProvider timeProvider)
        {
            _dbContext = dbContext;
            _timeProvider = timeProvider;
        }

        public async Task RecordAsync(CriticalActionAuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(entry.ActionType))
            {
                return;
            }

            var audit = new CriticalActionAudit
            {
                ActionType = entry.ActionType.Trim(),
                ResourceType = Normalize(entry.ResourceType),
                ResourceId = Normalize(entry.ResourceId),
                ActorName = string.IsNullOrWhiteSpace(entry.ActorName) ? "Unknown" : entry.ActorName.Trim(),
                ActorUserId = Normalize(entry.ActorUserId),
                IsSuccess = entry.IsSuccess,
                Details = NormalizeDetail(entry.Details),
                OccurredOn = _timeProvider.GetUtcNow()
            };

            _dbContext.CriticalActionAudits.Add(audit);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? NormalizeDetail(string? detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return null;
            }

            var trimmed = detail.Trim();
            return trimmed.Length > 512 ? trimmed[..512] : trimmed;
        }
    }
}
