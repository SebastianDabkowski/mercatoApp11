using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public static class SecurityIncidentStatuses
    {
        public const string New = "New";
        public const string Triaged = "Triaged";
        public const string Investigating = "Investigating";
        public const string Resolved = "Resolved";
        public const string Closed = "Closed";

        public static readonly string[] All =
        [
            New,
            Triaged,
            Investigating,
            Resolved,
            Closed
        ];

        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return New;
            }

            var trimmed = value.Trim();
            var match = All.FirstOrDefault(s => s.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
            return match ?? trimmed;
        }
    }

    public record SecurityIncidentDetection(
        string Source,
        string Rule,
        string? Severity,
        string? Summary);

    public record SecurityIncidentStatusUpdate(
        int IncidentId,
        string Status,
        string ActorUserId,
        string ActorName,
        string? Notes);

    public record SecurityIncidentFilters
    {
        public DateTimeOffset? From { get; init; }

        public DateTimeOffset? To { get; init; }

        public string? Severity { get; init; }

        public string? Status { get; init; }

        public string? Source { get; init; }

        public string? Rule { get; init; }
    }

    public record SecurityIncidentView(
        int Id,
        string Source,
        string Rule,
        string Severity,
        string Status,
        string? Summary,
        DateTimeOffset DetectedOn,
        DateTimeOffset? LastStatusOn,
        string? LastStatusBy,
        string? ResolutionNotes);

    public record SecurityIncidentStatusEntry(
        DateTimeOffset ChangedOn,
        string Status,
        string ActorName,
        string? ActorUserId,
        string? Notes);

    public record SecurityIncidentOperationResult(bool Success, SecurityIncidentView? Incident, List<string> Errors)
    {
        public static SecurityIncidentOperationResult Failed(params string[] errors) =>
            new(false, null, errors.ToList());

        public static SecurityIncidentOperationResult Succeeded(SecurityIncidentView incident) =>
            new(true, incident, new List<string>());
    }

    public record SecurityIncidentExportResult(byte[] Content, int RowCount);

    public class SecurityIncidentService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly SecurityIncidentOptions _options;
        private readonly TimeProvider _clock;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<SecurityIncidentService>? _logger;

        public SecurityIncidentService(
            ApplicationDbContext dbContext,
            SecurityIncidentOptions options,
            TimeProvider clock,
            IEmailSender emailSender,
            ILogger<SecurityIncidentService>? logger = null)
        {
            _dbContext = dbContext;
            _options = options;
            _clock = clock;
            _emailSender = emailSender;
            _logger = logger;
        }

        public async Task<SecurityIncidentOperationResult> RecordDetectionAsync(
            SecurityIncidentDetection detection,
            CancellationToken cancellationToken = default)
        {
            var errors = ValidateDetection(detection);
            if (errors.Count > 0)
            {
                return SecurityIncidentOperationResult.Failed(errors.ToArray());
            }

            var now = _clock.GetUtcNow();
            var severity = NormalizeSeverity(detection.Rule, detection.Severity);
            var incident = new SecurityIncident
            {
                Source = NormalizeLabel(detection.Source, 64)!,
                Rule = NormalizeLabel(detection.Rule, 128)!,
                Severity = severity,
                Status = SecurityIncidentStatuses.New,
                Summary = Trim(detection.Summary, 512),
                DetectedOn = now,
                LastStatusOn = now,
                LastStatusBy = "System",
                ResolutionNotes = null
            };

            _dbContext.SecurityIncidents.Add(incident);
            _dbContext.SecurityIncidentStatusChanges.Add(new SecurityIncidentStatusChange
            {
                Incident = incident,
                Status = incident.Status,
                ActorName = "System",
                ChangedOn = now,
                Notes = Trim(detection.Summary, 512)
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            await SendAlertsIfNeededAsync(incident, cancellationToken);

            return SecurityIncidentOperationResult.Succeeded(Map(incident));
        }

        public async Task<SecurityIncidentOperationResult> UpdateStatusAsync(
            SecurityIncidentStatusUpdate update,
            CancellationToken cancellationToken = default)
        {
            var incident = await _dbContext.SecurityIncidents
                .FirstOrDefaultAsync(i => i.Id == update.IncidentId, cancellationToken);

            if (incident == null)
            {
                return SecurityIncidentOperationResult.Failed("Incident not found.");
            }

            var normalizedStatus = SecurityIncidentStatuses.Normalize(update.Status);
            if (!SecurityIncidentStatuses.All.Any(s => s.Equals(normalizedStatus, StringComparison.OrdinalIgnoreCase)))
            {
                return SecurityIncidentOperationResult.Failed("Unsupported status value.");
            }

            var now = _clock.GetUtcNow();
            incident.Status = normalizedStatus;
            incident.LastStatusOn = now;
            incident.LastStatusBy = NormalizeActor(update.ActorName);
            incident.LastStatusByUserId = NormalizeId(update.ActorUserId);
            incident.ResolutionNotes = string.IsNullOrWhiteSpace(update.Notes)
                ? incident.ResolutionNotes
                : Trim(update.Notes, 1024);

            _dbContext.SecurityIncidentStatusChanges.Add(new SecurityIncidentStatusChange
            {
                IncidentId = incident.Id,
                Status = incident.Status,
                ActorName = incident.LastStatusBy ?? "Unknown",
                ActorUserId = incident.LastStatusByUserId,
                Notes = Trim(update.Notes, 512),
                ChangedOn = now
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            return SecurityIncidentOperationResult.Succeeded(Map(incident));
        }

        public async Task<SecurityIncidentView?> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var incident = await _dbContext.SecurityIncidents.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

            return incident == null ? null : Map(incident);
        }

        public async Task<List<SecurityIncidentStatusEntry>> GetHistoryAsync(
            int incidentId,
            CancellationToken cancellationToken = default)
        {
            var history = await _dbContext.SecurityIncidentStatusChanges.AsNoTracking()
                .Where(s => s.IncidentId == incidentId)
                .OrderByDescending(s => s.ChangedOn)
                .ToListAsync(cancellationToken);

            return history.Select(h => new SecurityIncidentStatusEntry(
                h.ChangedOn,
                h.Status,
                h.ActorName,
                h.ActorUserId,
                h.Notes)).ToList();
        }

        public async Task<PagedResult<SecurityIncidentView>> GetAsync(
            SecurityIncidentFilters filters,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var normalizedPage = Math.Max(1, pageNumber);
            var normalizedPageSize = Math.Clamp(pageSize, 1, 200);

            var query = _dbContext.SecurityIncidents.AsNoTracking();

            if (filters.From.HasValue)
            {
                query = query.Where(i => i.DetectedOn >= filters.From.Value);
            }

            if (filters.To.HasValue)
            {
                query = query.Where(i => i.DetectedOn <= filters.To.Value);
            }

            if (!string.IsNullOrWhiteSpace(filters.Severity))
            {
                var severity = NormalizeSeverity(string.Empty, filters.Severity);
                query = query.Where(i => i.Severity == severity);
            }

            if (!string.IsNullOrWhiteSpace(filters.Status))
            {
                var status = SecurityIncidentStatuses.Normalize(filters.Status);
                query = query.Where(i => i.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(filters.Source))
            {
                var source = NormalizeLabel(filters.Source, 64);
                query = query.Where(i => i.Source == source);
            }

            if (!string.IsNullOrWhiteSpace(filters.Rule))
            {
                var rule = NormalizeLabel(filters.Rule, 128);
                query = query.Where(i => i.Rule == rule);
            }

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(i => i.DetectedOn)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);

            var mapped = items.Select(Map).ToList();
            return new PagedResult<SecurityIncidentView>
            {
                Items = mapped,
                TotalCount = total,
                PageNumber = normalizedPage,
                PageSize = normalizedPageSize
            };
        }

        public async Task<SecurityIncidentExportResult> ExportAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            var normalizedFrom = from <= to ? from : to;
            var normalizedTo = to >= from ? to : from;

            var incidents = await _dbContext.SecurityIncidents.AsNoTracking()
                .Where(i => i.DetectedOn >= normalizedFrom && i.DetectedOn <= normalizedTo)
                .OrderBy(i => i.DetectedOn)
                .ToListAsync(cancellationToken);

            var builder = new StringBuilder();
            builder.AppendLine("Id,DetectedOn,Source,Rule,Severity,Status,Summary,ResolutionNotes");

            foreach (var incident in incidents)
            {
                var detectedOn = incident.DetectedOn.ToString("O", CultureInfo.InvariantCulture);
                builder.Append(incident.Id).Append(',')
                    .Append(Escape(detectedOn)).Append(',')
                    .Append(Escape(incident.Source)).Append(',')
                    .Append(Escape(incident.Rule)).Append(',')
                    .Append(Escape(incident.Severity)).Append(',')
                    .Append(Escape(incident.Status)).Append(',')
                    .Append(Escape(incident.Summary)).Append(',')
                    .AppendLine(Escape(incident.ResolutionNotes));
            }

            return new SecurityIncidentExportResult(Encoding.UTF8.GetBytes(builder.ToString()), incidents.Count);
        }

        private List<string> ValidateDetection(SecurityIncidentDetection detection)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(detection.Source))
            {
                errors.Add("Source is required.");
            }

            if (string.IsNullOrWhiteSpace(detection.Rule))
            {
                errors.Add("Rule is required.");
            }

            return errors;
        }

        private string NormalizeSeverity(string rule, string? severity)
        {
            var candidate = !string.IsNullOrWhiteSpace(severity)
                ? severity
                : ResolveRuleSeverity(rule);

            var normalized = candidate?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return _options.DefaultSeverity;
            }

            var match = _options.SeverityOrder.FirstOrDefault(s => s.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            return match ?? normalized;
        }

        private string ResolveRuleSeverity(string rule)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                return _options.DefaultSeverity;
            }

            if (_options.RuleSeverities.TryGetValue(rule.Trim(), out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }

            return _options.DefaultSeverity;
        }

        private async Task SendAlertsIfNeededAsync(SecurityIncident incident, CancellationToken cancellationToken)
        {
            if (_options.AlertRecipients.Count == 0)
            {
                return;
            }

            if (!MeetsAlertThreshold(incident.Severity))
            {
                return;
            }

            var subject = $"Security incident {incident.Severity}: {incident.Rule}";
            var body = new StringBuilder()
                .AppendLine("A security incident was detected.")
                .AppendLine($"Id: {incident.Id}")
                .AppendLine($"Source: {incident.Source}")
                .AppendLine($"Rule: {incident.Rule}")
                .AppendLine($"Severity: {incident.Severity}")
                .AppendLine($"Status: {incident.Status}")
                .AppendLine($"DetectedOn: {incident.DetectedOn:O}");

            if (!string.IsNullOrWhiteSpace(incident.Summary))
            {
                body.AppendLine().AppendLine("Summary:").AppendLine(incident.Summary);
            }

            foreach (var recipient in _options.AlertRecipients.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await _emailSender.SendEmailAsync(recipient, subject, body.ToString());
            }

            _logger?.LogInformation("Alerted {RecipientCount} contacts for incident {IncidentId} ({Severity}).", _options.AlertRecipients.Count, incident.Id, incident.Severity);
        }

        private bool MeetsAlertThreshold(string severity)
        {
            if (_options.SeverityOrder.Count == 0)
            {
                return false;
            }

            var thresholdIndex = _options.SeverityOrder.FindIndex(s => s.Equals(_options.AlertSeverityThreshold, StringComparison.OrdinalIgnoreCase));
            if (thresholdIndex < 0)
            {
                thresholdIndex = _options.SeverityOrder.Count - 1;
            }

            var severityIndex = _options.SeverityOrder.FindIndex(s => s.Equals(severity, StringComparison.OrdinalIgnoreCase));
            if (severityIndex < 0)
            {
                return false;
            }

            return severityIndex >= thresholdIndex;
        }

        private static string? NormalizeLabel(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
        }

        private static string? NormalizeId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length > 450 ? trimmed[..450] : trimmed;
        }

        private static string NormalizeActor(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unknown";
            }

            var trimmed = value.Trim();
            return trimmed.Length > 256 ? trimmed[..256] : trimmed;
        }

        private static string? Trim(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            var sanitized = value.Replace("\"", "\"\"");
            return $"\"{sanitized}\"";
        }

        private static SecurityIncidentView Map(SecurityIncident incident) =>
            new(
                incident.Id,
                incident.Source,
                incident.Rule,
                incident.Severity,
                incident.Status,
                incident.Summary,
                incident.DetectedOn,
                incident.LastStatusOn,
                incident.LastStatusBy,
                incident.ResolutionNotes);
    }
}
