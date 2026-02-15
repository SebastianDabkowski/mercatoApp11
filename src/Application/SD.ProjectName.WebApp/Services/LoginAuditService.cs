using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public record LoginAuditEntry
    {
        public string? UserId { get; init; }

        public string? Email { get; init; }

        public string EventType { get; init; } = string.Empty;

        public bool IsSuccess { get; init; }

        public string? IpAddress { get; init; }

        public string? UserAgent { get; init; }
    }

    public record LoginAuditResult
    {
        public bool IsUnusual { get; init; }
    }

    public interface ILoginAuditService
    {
        Task<LoginAuditResult> RecordAsync(LoginAuditEntry entry, CancellationToken cancellationToken = default);
    }

    public class LoginAuditService : ILoginAuditService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly TimeProvider _timeProvider;
        private readonly IEmailSender _emailSender;
        private readonly IOptions<SecurityOptions> _options;
        private readonly SecurityIncidentService? _incidentService;

        public LoginAuditService(
            ApplicationDbContext dbContext,
            TimeProvider timeProvider,
            IEmailSender emailSender,
            IOptions<SecurityOptions> options,
            SecurityIncidentService? incidentService = null)
        {
            _dbContext = dbContext;
            _timeProvider = timeProvider;
            _emailSender = emailSender;
            _options = options;
            _incidentService = incidentService;
        }

        public async Task<LoginAuditResult> RecordAsync(LoginAuditEntry entry, CancellationToken cancellationToken = default)
        {
            var now = _timeProvider.GetUtcNow();
            var isUnusual = await IsUnusualAsync(entry, cancellationToken);

            var audit = new LoginAudit
            {
                UserId = entry.UserId,
                Email = entry.Email,
                EventType = entry.EventType,
                IsSuccess = entry.IsSuccess,
                IsUnusual = isUnusual,
                OccurredOn = now,
                ExpiresOn = now.AddDays(Math.Max(1, _options.Value.LoginHistoryRetentionDays)),
                IpAddress = entry.IpAddress,
                UserAgent = entry.UserAgent
            };

            _dbContext.LoginAudits.Add(audit);

            if (!string.IsNullOrEmpty(entry.UserId) && entry.IsSuccess)
            {
                var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == entry.UserId, cancellationToken);
                if (user != null)
                {
                    user.LastLoginOn = now;
                    user.LastLoginIp = entry.IpAddress;
                    if (entry.EventType == LoginEventTypes.TwoFactorSuccess && user.TwoFactorEnabledOn == null)
                    {
                        user.TwoFactorEnabledOn = now;
                        user.TwoFactorMethod = _options.Value.TwoFactorProvider;
                    }
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (isUnusual && _options.Value.AlertOnNewIp && !string.IsNullOrEmpty(entry.Email))
            {
                await _emailSender.SendEmailAsync(
                    entry.Email,
                    "Unusual login detected",
                    "We noticed a login from a new location or device. If this wasn't you, please reset your password.");
            }

            if (_incidentService != null)
            {
                if (string.Equals(entry.EventType, LoginEventTypes.LockedOut, StringComparison.OrdinalIgnoreCase))
                {
                    await _incidentService.RecordDetectionAsync(
                        new SecurityIncidentDetection(
                            "Authentication",
                            "authentication:lockout",
                            null,
                            "Account lockout triggered after repeated failed login attempts."),
                        cancellationToken);
                }
                else if (isUnusual && entry.IsSuccess)
                {
                    await _incidentService.RecordDetectionAsync(
                        new SecurityIncidentDetection(
                            "Authentication",
                            "authentication:unusual-login",
                            null,
                            "Successful login from a new location detected."),
                        cancellationToken);
                }
            }

            return new LoginAuditResult { IsUnusual = isUnusual };
        }

        private async Task<bool> IsUnusualAsync(LoginAuditEntry entry, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(entry.UserId) || string.IsNullOrWhiteSpace(entry.IpAddress))
            {
                return false;
            }

            var lastSuccess = await _dbContext.LoginAudits
                .Where(a => a.UserId == entry.UserId && a.IsSuccess)
                .OrderByDescending(a => a.OccurredOn)
                .FirstOrDefaultAsync(cancellationToken);

            return lastSuccess != null &&
                   !string.Equals(lastSuccess.IpAddress, entry.IpAddress, StringComparison.OrdinalIgnoreCase);
        }
    }
}
