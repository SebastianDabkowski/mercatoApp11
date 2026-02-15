using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services;

public class ProcessingActivity
{
    public int Id { get; set; }

    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string Purpose { get; set; } = string.Empty;

    [MaxLength(512)]
    public string LegalBasis { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string DataCategories { get; set; } = string.Empty;

    [MaxLength(512)]
    public string DataSubjects { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string Processors { get; set; } = string.Empty;

    [MaxLength(256)]
    public string RetentionPeriod { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? DataTransfers { get; set; }

    [MaxLength(1024)]
    public string? SecurityMeasures { get; set; }

    public DateTimeOffset CreatedOn { get; set; }

    [MaxLength(450)]
    public string? CreatedById { get; set; }

    [MaxLength(256)]
    public string? CreatedByName { get; set; }

    public DateTimeOffset? UpdatedOn { get; set; }

    [MaxLength(450)]
    public string? UpdatedById { get; set; }

    [MaxLength(256)]
    public string? UpdatedByName { get; set; }

    public ICollection<ProcessingActivityRevision> Revisions { get; set; } = new List<ProcessingActivityRevision>();
}

public class ProcessingActivityRevision
{
    public int Id { get; set; }

    public int ProcessingActivityId { get; set; }

    public ProcessingActivity? ProcessingActivity { get; set; }

    [MaxLength(32)]
    public string ChangeType { get; set; } = "Updated";

    [MaxLength(512)]
    public string? ChangedFields { get; set; }

    public string SnapshotJson { get; set; } = string.Empty;

    public DateTimeOffset ChangedOn { get; set; }

    [MaxLength(450)]
    public string? ChangedById { get; set; }

    [MaxLength(256)]
    public string? ChangedByName { get; set; }
}

public record ProcessingActivityInput(
    int? Id,
    string Name,
    string Purpose,
    string LegalBasis,
    string DataCategories,
    string DataSubjects,
    string Processors,
    string RetentionPeriod,
    string? DataTransfers,
    string? SecurityMeasures);

public record ProcessingActivityView(
    int Id,
    string Name,
    string Purpose,
    string LegalBasis,
    string DataCategories,
    string DataSubjects,
    string Processors,
    string RetentionPeriod,
    string? DataTransfers,
    string? SecurityMeasures,
    DateTimeOffset CreatedOn,
    string? CreatedByName,
    DateTimeOffset? UpdatedOn,
    string? UpdatedByName);

public record ProcessingActivityHistoryEntry(
    DateTimeOffset ChangedOn,
    string ChangeType,
    string? ChangedByName,
    string? ChangedFields,
    ProcessingActivitySnapshot Snapshot);

public record ProcessingActivitySnapshot(
    string Name,
    string Purpose,
    string LegalBasis,
    string DataCategories,
    string DataSubjects,
    string Processors,
    string RetentionPeriod,
    string? DataTransfers,
    string? SecurityMeasures);

public record ProcessingActivityOperationResult(bool Success, ProcessingActivityView? Activity, List<string> Errors)
{
    public static ProcessingActivityOperationResult Failed(params string[] errors) =>
        new(false, null, errors.ToList());

    public static ProcessingActivityOperationResult Succeeded(ProcessingActivityView activity) =>
        new(true, activity, new List<string>());
}

public class ProcessingActivityRegistryService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ProcessingActivityRegistryService(ApplicationDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<List<ProcessingActivityView>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.ProcessingActivities
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        return items.Select(MapToView).ToList();
    }

    public async Task<ProcessingActivityView?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ProcessingActivities
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        return entity == null ? null : MapToView(entity);
    }

    public async Task<List<ProcessingActivityHistoryEntry>> GetHistoryAsync(int id, CancellationToken cancellationToken = default)
    {
        var revisions = await _dbContext.ProcessingActivityRevisions
            .AsNoTracking()
            .Where(r => r.ProcessingActivityId == id)
            .OrderByDescending(r => r.ChangedOn)
            .ToListAsync(cancellationToken);

        var entries = new List<ProcessingActivityHistoryEntry>();
        foreach (var revision in revisions)
        {
            var snapshot = DeserializeSnapshot(revision.SnapshotJson);
            if (snapshot == null)
            {
                continue;
            }

            entries.Add(new ProcessingActivityHistoryEntry(
                revision.ChangedOn,
                revision.ChangeType,
                string.IsNullOrWhiteSpace(revision.ChangedByName) ? "System" : revision.ChangedByName,
                revision.ChangedFields,
                snapshot));
        }

        return entries;
    }

    public async Task<ProcessingActivityOperationResult> CreateAsync(
        ProcessingActivityInput input,
        string actorId,
        string? actorName,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(input);
        var errors = Validate(normalized);
        if (errors.Count > 0)
        {
            return ProcessingActivityOperationResult.Failed(errors.ToArray());
        }

        var now = _timeProvider.GetUtcNow();
        var entity = new ProcessingActivity
        {
            Name = normalized.Name,
            Purpose = normalized.Purpose,
            LegalBasis = normalized.LegalBasis,
            DataCategories = normalized.DataCategories,
            DataSubjects = normalized.DataSubjects,
            Processors = normalized.Processors,
            RetentionPeriod = normalized.RetentionPeriod,
            DataTransfers = normalized.DataTransfers,
            SecurityMeasures = normalized.SecurityMeasures,
            CreatedOn = now,
            CreatedById = actorId,
            CreatedByName = actorName,
            UpdatedOn = now,
            UpdatedById = actorId,
            UpdatedByName = actorName
        };

        var snapshot = CreateSnapshot(entity);
        entity.Revisions.Add(new ProcessingActivityRevision
        {
            ChangeType = "Created",
            SnapshotJson = JsonSerializer.Serialize(snapshot, _jsonOptions),
            ChangedOn = now,
            ChangedById = actorId,
            ChangedByName = actorName,
            ChangedFields = "All fields"
        });

        _dbContext.ProcessingActivities.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ProcessingActivityOperationResult.Succeeded(MapToView(entity));
    }

    public async Task<ProcessingActivityOperationResult> UpdateAsync(
        ProcessingActivityInput input,
        string actorId,
        string? actorName,
        CancellationToken cancellationToken = default)
    {
        if (!input.Id.HasValue)
        {
            return ProcessingActivityOperationResult.Failed("Processing activity id is required.");
        }

        var normalized = Normalize(input);
        var errors = Validate(normalized);
        if (errors.Count > 0)
        {
            return ProcessingActivityOperationResult.Failed(errors.ToArray());
        }

        var entity = await _dbContext.ProcessingActivities
            .FirstOrDefaultAsync(a => a.Id == normalized.Id!.Value, cancellationToken);

        if (entity == null)
        {
            return ProcessingActivityOperationResult.Failed("Processing activity not found.");
        }

        var changedFields = ApplyChanges(entity, normalized);
        if (changedFields.Count == 0)
        {
            return ProcessingActivityOperationResult.Failed("No changes detected.");
        }

        var now = _timeProvider.GetUtcNow();
        entity.UpdatedOn = now;
        entity.UpdatedById = actorId;
        entity.UpdatedByName = actorName;

        var snapshot = CreateSnapshot(entity);
        entity.Revisions.Add(new ProcessingActivityRevision
        {
            ProcessingActivityId = entity.Id,
            ChangeType = "Updated",
            SnapshotJson = JsonSerializer.Serialize(snapshot, _jsonOptions),
            ChangedOn = now,
            ChangedById = actorId,
            ChangedByName = actorName,
            ChangedFields = string.Join(", ", changedFields)
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ProcessingActivityOperationResult.Succeeded(MapToView(entity));
    }

    public async Task<byte[]> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        var activities = await _dbContext.ProcessingActivities
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        if (activities.Count == 0)
        {
            return Array.Empty<byte>();
        }

        var sb = new StringBuilder();
        sb.AppendLine("Name,Purpose,Legal basis,Data categories,Data subjects,Processors,Retention,Data transfers,Security measures,Last updated,Updated by");
        foreach (var activity in activities)
        {
            sb.AppendLine(string.Join(",",
                Csv(activity.Name),
                Csv(activity.Purpose),
                Csv(activity.LegalBasis),
                Csv(activity.DataCategories),
                Csv(activity.DataSubjects),
                Csv(activity.Processors),
                Csv(activity.RetentionPeriod),
                Csv(activity.DataTransfers),
                Csv(activity.SecurityMeasures),
                Csv((activity.UpdatedOn ?? activity.CreatedOn).ToString("u")),
                Csv(activity.UpdatedByName ?? activity.CreatedByName ?? "System")));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static ProcessingActivityView MapToView(ProcessingActivity entity) =>
        new(
            entity.Id,
            entity.Name,
            entity.Purpose,
            entity.LegalBasis,
            entity.DataCategories,
            entity.DataSubjects,
            entity.Processors,
            entity.RetentionPeriod,
            entity.DataTransfers,
            entity.SecurityMeasures,
            entity.CreatedOn,
            entity.CreatedByName,
            entity.UpdatedOn,
            entity.UpdatedByName);

    private static ProcessingActivityInput Normalize(ProcessingActivityInput input) =>
        input with
        {
            Name = (input.Name ?? string.Empty).Trim(),
            Purpose = (input.Purpose ?? string.Empty).Trim(),
            LegalBasis = (input.LegalBasis ?? string.Empty).Trim(),
            DataCategories = (input.DataCategories ?? string.Empty).Trim(),
            DataSubjects = (input.DataSubjects ?? string.Empty).Trim(),
            Processors = (input.Processors ?? string.Empty).Trim(),
            RetentionPeriod = (input.RetentionPeriod ?? string.Empty).Trim(),
            DataTransfers = string.IsNullOrWhiteSpace(input.DataTransfers) ? null : input.DataTransfers.Trim(),
            SecurityMeasures = string.IsNullOrWhiteSpace(input.SecurityMeasures) ? null : input.SecurityMeasures.Trim()
        };

    private static List<string> Validate(ProcessingActivityInput input)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors.Add("A title for the processing activity is required.");
        }
        ValidateLength(input.Name, 256, "Activity title", errors);

        if (string.IsNullOrWhiteSpace(input.Purpose))
        {
            errors.Add("Purpose is required.");
        }
        ValidateLength(input.Purpose, 1024, "Purpose", errors);

        if (string.IsNullOrWhiteSpace(input.LegalBasis))
        {
            errors.Add("Legal basis is required.");
        }
        ValidateLength(input.LegalBasis, 512, "Legal basis", errors);

        if (string.IsNullOrWhiteSpace(input.DataCategories))
        {
            errors.Add("Categories of personal data are required.");
        }
        ValidateLength(input.DataCategories, 1024, "Categories of personal data", errors);

        if (string.IsNullOrWhiteSpace(input.DataSubjects))
        {
            errors.Add("Data subject categories are required.");
        }
        ValidateLength(input.DataSubjects, 512, "Data subjects", errors);

        if (string.IsNullOrWhiteSpace(input.Processors))
        {
            errors.Add("Processors or recipients are required.");
        }
        ValidateLength(input.Processors, 1024, "Processors / recipients", errors);

        if (string.IsNullOrWhiteSpace(input.RetentionPeriod))
        {
            errors.Add("Retention period is required.");
        }
        ValidateLength(input.RetentionPeriod, 256, "Retention period", errors);

        ValidateLength(input.DataTransfers, 512, "International transfers", errors);
        ValidateLength(input.SecurityMeasures, 1024, "Security measures", errors);

        return errors;

        static void ValidateLength(string? value, int maxLength, string fieldName, List<string> collector)
        {
            if (!string.IsNullOrEmpty(value) && value.Length > maxLength)
            {
                collector.Add($"{fieldName} must be {maxLength} characters or fewer.");
            }
        }
    }

    private static List<string> ApplyChanges(ProcessingActivity entity, ProcessingActivityInput input)
    {
        var changes = new List<string>();

        if (!string.Equals(entity.Name, input.Name, StringComparison.Ordinal))
        {
            entity.Name = input.Name;
            changes.Add(nameof(entity.Name));
        }

        if (!string.Equals(entity.Purpose, input.Purpose, StringComparison.Ordinal))
        {
            entity.Purpose = input.Purpose;
            changes.Add(nameof(entity.Purpose));
        }

        if (!string.Equals(entity.LegalBasis, input.LegalBasis, StringComparison.Ordinal))
        {
            entity.LegalBasis = input.LegalBasis;
            changes.Add(nameof(entity.LegalBasis));
        }

        if (!string.Equals(entity.DataCategories, input.DataCategories, StringComparison.Ordinal))
        {
            entity.DataCategories = input.DataCategories;
            changes.Add(nameof(entity.DataCategories));
        }

        if (!string.Equals(entity.DataSubjects, input.DataSubjects, StringComparison.Ordinal))
        {
            entity.DataSubjects = input.DataSubjects;
            changes.Add(nameof(entity.DataSubjects));
        }

        if (!string.Equals(entity.Processors, input.Processors, StringComparison.Ordinal))
        {
            entity.Processors = input.Processors;
            changes.Add(nameof(entity.Processors));
        }

        if (!string.Equals(entity.RetentionPeriod, input.RetentionPeriod, StringComparison.Ordinal))
        {
            entity.RetentionPeriod = input.RetentionPeriod;
            changes.Add(nameof(entity.RetentionPeriod));
        }

        if (!string.Equals(entity.DataTransfers ?? string.Empty, input.DataTransfers ?? string.Empty, StringComparison.Ordinal))
        {
            entity.DataTransfers = input.DataTransfers;
            changes.Add(nameof(entity.DataTransfers));
        }

        if (!string.Equals(entity.SecurityMeasures ?? string.Empty, input.SecurityMeasures ?? string.Empty, StringComparison.Ordinal))
        {
            entity.SecurityMeasures = input.SecurityMeasures;
            changes.Add(nameof(entity.SecurityMeasures));
        }

        return changes;
    }

    private ProcessingActivitySnapshot CreateSnapshot(ProcessingActivity entity) =>
        new(
            entity.Name,
            entity.Purpose,
            entity.LegalBasis,
            entity.DataCategories,
            entity.DataSubjects,
            entity.Processors,
            entity.RetentionPeriod,
            entity.DataTransfers,
            entity.SecurityMeasures);

    private ProcessingActivitySnapshot? DeserializeSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProcessingActivitySnapshot>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string Csv(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.Contains('"') || safe.Contains(',') || safe.Contains('\n') || safe.Contains('\r'))
        {
            safe = "\"" + safe.Replace("\"", "\"\"") + "\"";
        }

        return safe;
    }
}
