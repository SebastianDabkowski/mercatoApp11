using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services;

public class FeatureFlag
{
    public int Id { get; set; }

    [MaxLength(128)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Description { get; set; }

    public bool DefaultEnabled { get; set; }

    public DateTimeOffset CreatedOn { get; set; }

    [MaxLength(450)]
    public string? CreatedBy { get; set; }

    [MaxLength(256)]
    public string? CreatedByName { get; set; }

    public DateTimeOffset? UpdatedOn { get; set; }

    [MaxLength(450)]
    public string? UpdatedBy { get; set; }

    [MaxLength(256)]
    public string? UpdatedByName { get; set; }

    public ICollection<FeatureFlagEnvironment> Environments { get; set; } = new List<FeatureFlagEnvironment>();
}

public class FeatureFlagEnvironment
{
    public int Id { get; set; }

    public int FlagId { get; set; }

    public FeatureFlag? Flag { get; set; }

    [MaxLength(32)]
    public string Environment { get; set; } = "Production";

    public bool Enabled { get; set; }

    public string? TargetingJson { get; set; }

    public DateTimeOffset CreatedOn { get; set; }

    public DateTimeOffset? UpdatedOn { get; set; }
}

public class FeatureFlagAudit
{
    public int Id { get; set; }

    public int FlagId { get; set; }

    [MaxLength(32)]
    public string Action { get; set; } = "Updated";

    public string SnapshotJson { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? ActorId { get; set; }

    [MaxLength(256)]
    public string? ActorName { get; set; }

    public DateTimeOffset ChangedOn { get; set; }
}

public record FeatureFlagTargetingRule(
    bool InternalOnly,
    List<string> AllowedUsers,
    List<string> AllowedSellers,
    int? PercentageRollout);

public record FeatureFlagEnvironmentInput(
    string Environment,
    bool Enabled,
    FeatureFlagTargetingInput Targeting);

public record FeatureFlagTargetingInput(
    bool InternalOnly,
    List<string> Users,
    List<string> Sellers,
    int? PercentageRollout);

public record FeatureFlagInput(
    int? Id,
    string Key,
    string Name,
    string? Description,
    bool DefaultEnabled,
    List<FeatureFlagEnvironmentInput> Environments);

public record FeatureFlagEnvironmentView(
    int? Id,
    string Environment,
    bool Enabled,
    FeatureFlagTargetingRule Targeting,
    DateTimeOffset CreatedOn,
    DateTimeOffset? UpdatedOn);

public record FeatureFlagView(
    int Id,
    string Key,
    string Name,
    string? Description,
    bool DefaultEnabled,
    List<FeatureFlagEnvironmentView> Environments,
    DateTimeOffset CreatedOn,
    DateTimeOffset? UpdatedOn);

public record FeatureFlagOperationResult(bool Success, FeatureFlagView? Flag, List<string> Errors)
{
    public static FeatureFlagOperationResult Failed(params string[] errors) => new(false, null, errors.ToList());

    public static FeatureFlagOperationResult Succeeded(FeatureFlagView flag) => new(true, flag, new List<string>());
}

public record FeatureFlagEvaluationContext(
    string Key,
    string Environment,
    string? UserId,
    string? SellerId,
    bool IsInternalUser);

public interface IFeatureFlagEvaluator
{
    Task<bool> EvaluateAsync(FeatureFlagEvaluationContext context, CancellationToken cancellationToken = default);
}

public class FeatureFlagService : IFeatureFlagEvaluator
{
    private const string DefaultEnvironment = "Production";
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<FeatureFlagService>? _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _cacheLock = new();
    private List<FeatureFlagView> _cachedFlags = new();

    public FeatureFlagService(ApplicationDbContext dbContext, TimeProvider timeProvider, ILogger<FeatureFlagService>? logger = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<List<FeatureFlagView>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var flags = await _dbContext.FeatureFlags
            .Include(f => f.Environments)
            .AsNoTracking()
            .OrderBy(f => f.Key)
            .ToListAsync(cancellationToken);

        var views = flags.Select(MapToView).ToList();
        UpdateCache(views);
        return views;
    }

    public async Task<FeatureFlagView?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        var cached = GetCachedFlags();
        var cachedMatch = cached.FirstOrDefault(f => f.Id == id);
        if (cachedMatch != null)
        {
            return cachedMatch;
        }

        var flag = await _dbContext.FeatureFlags
            .Include(f => f.Environments)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        return flag == null ? null : MapToView(flag);
    }

    public async Task<FeatureFlagOperationResult> SaveAsync(
        FeatureFlagInput input,
        string actorId,
        string? actorName,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return FeatureFlagOperationResult.Failed(errors.ToArray());
        }

        var normalizedKey = NormalizeKey(input.Key);
        var normalizedName = input.Name?.Trim() ?? string.Empty;
        var environments = NormalizeEnvironments(input.Environments, input.DefaultEnabled);
        var existingWithKey = await _dbContext.FeatureFlags
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id != input.Id && f.Key.ToLower() == normalizedKey.ToLower(), cancellationToken);

        if (existingWithKey != null)
        {
            return FeatureFlagOperationResult.Failed("A feature flag with the same key already exists.");
        }

        FeatureFlag? entity = null;
        if (input.Id.HasValue)
        {
            entity = await _dbContext.FeatureFlags
                .Include(f => f.Environments)
                .FirstOrDefaultAsync(f => f.Id == input.Id.Value, cancellationToken);
        }

        var now = _timeProvider.GetUtcNow();
        var isNew = entity == null;
        if (entity == null)
        {
            entity = new FeatureFlag
            {
                CreatedBy = actorId,
                CreatedByName = actorName,
                CreatedOn = now
            };
            _dbContext.FeatureFlags.Add(entity);
        }

        entity.Key = normalizedKey;
        entity.Name = normalizedName;
        entity.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        entity.DefaultEnabled = input.DefaultEnabled;
        entity.UpdatedBy = actorId;
        entity.UpdatedByName = actorName;
        entity.UpdatedOn = now;

        ApplyEnvironmentChanges(entity, environments, now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var view = MapToView(entity);
        await RecordAuditAsync(entity.Id, isNew ? "Created" : "Updated", actorId, actorName, cancellationToken);
        InvalidateCache();
        return FeatureFlagOperationResult.Succeeded(view);
    }

    public async Task<FeatureFlagOperationResult> SetEnvironmentStateAsync(
        int flagId,
        string environment,
        bool enabled,
        string actorId,
        string? actorName,
        CancellationToken cancellationToken = default)
    {
        var target = await _dbContext.FeatureFlags
            .Include(f => f.Environments)
            .FirstOrDefaultAsync(f => f.Id == flagId, cancellationToken);

        if (target == null)
        {
            return FeatureFlagOperationResult.Failed("Feature flag not found.");
        }

        var now = _timeProvider.GetUtcNow();
        var envName = NormalizeEnvironment(environment);
        var env = target.Environments.FirstOrDefault(e => string.Equals(e.Environment, envName, StringComparison.OrdinalIgnoreCase));
        if (env == null)
        {
            env = new FeatureFlagEnvironment
            {
                Environment = envName,
                CreatedOn = now,
                Enabled = enabled,
                TargetingJson = SerializeTargeting(DefaultTargeting())
            };
            target.Environments.Add(env);
        }
        else
        {
            env.Enabled = enabled;
            env.UpdatedOn = now;
        }

        target.UpdatedBy = actorId;
        target.UpdatedByName = actorName;
        target.UpdatedOn = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecordAuditAsync(target.Id, "Toggled", actorId, actorName, cancellationToken);
        InvalidateCache();
        return FeatureFlagOperationResult.Succeeded(MapToView(target));
    }

    public async Task<bool> EvaluateAsync(FeatureFlagEvaluationContext context, CancellationToken cancellationToken = default)
    {
        var flags = GetCachedFlags();
        if (flags.Count == 0)
        {
            flags = await GetAllAsync(cancellationToken);
        }

        var flag = flags.FirstOrDefault(f => string.Equals(f.Key, context.Key, StringComparison.OrdinalIgnoreCase));
        if (flag == null)
        {
            return false;
        }

        var environment = NormalizeEnvironment(context.Environment);
        var env = flag.Environments.FirstOrDefault(e => string.Equals(e.Environment, environment, StringComparison.OrdinalIgnoreCase))
            ?? flag.Environments.FirstOrDefault(e => string.Equals(e.Environment, DefaultEnvironment, StringComparison.OrdinalIgnoreCase))
            ?? new FeatureFlagEnvironmentView(null, environment, flag.DefaultEnabled, DefaultTargeting(), flag.CreatedOn, flag.UpdatedOn);

        var targeting = env.Targeting ?? DefaultTargeting();
        if (targeting.InternalOnly && !context.IsInternalUser)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(context.UserId) && targeting.AllowedUsers.Any(u => string.Equals(u, context.UserId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(context.SellerId) && targeting.AllowedSellers.Any(s => string.Equals(s, context.SellerId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (targeting.PercentageRollout.HasValue)
        {
            var bucketKey = context.UserId ?? context.SellerId ?? context.Environment ?? context.Key;
            var bucket = ComputeBucket(bucketKey);
            return bucket < targeting.PercentageRollout.Value;
        }

        return env.Enabled;
    }

    private static List<string> Validate(FeatureFlagInput input)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Key))
        {
            errors.Add("Key is required.");
        }
        else if (input.Key.Length > 128)
        {
            errors.Add("Key must be 128 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors.Add("Name is required.");
        }
        else if (input.Name.Length > 256)
        {
            errors.Add("Name must be 256 characters or fewer.");
        }

        var environments = NormalizeEnvironments(input.Environments, input.DefaultEnabled);
        var duplicateEnvironments = environments
            .GroupBy(e => e.Environment, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateEnvironments.Count > 0)
        {
            errors.Add($"Duplicate environment entries: {string.Join(", ", duplicateEnvironments)}.");
        }

        foreach (var env in environments)
        {
            if (env.Targeting.PercentageRollout is < 0 or > 100)
            {
                errors.Add($"Rollout percentage for {env.Environment} must be between 0 and 100.");
            }
        }

        return errors;
    }

    private static string NormalizeKey(string key) => key.Trim();

    private static string NormalizeEnvironment(string? environment) =>
        string.IsNullOrWhiteSpace(environment) ? DefaultEnvironment : environment.Trim();

    private static List<FeatureFlagEnvironmentInput> NormalizeEnvironments(
        List<FeatureFlagEnvironmentInput> environments,
        bool defaultEnabled)
    {
        var normalized = new List<FeatureFlagEnvironmentInput>();
        foreach (var env in environments ?? new List<FeatureFlagEnvironmentInput>())
        {
            var name = NormalizeEnvironment(env.Environment);
            normalized.Add(new FeatureFlagEnvironmentInput(
                name,
                env.Enabled,
                NormalizeTargeting(env.Targeting)));
        }

        if (normalized.Count == 0)
        {
            normalized.Add(new FeatureFlagEnvironmentInput(DefaultEnvironment, defaultEnabled, NormalizeTargeting(null)));
        }

        return normalized;
    }

    private static FeatureFlagTargetingInput NormalizeTargeting(FeatureFlagTargetingInput? targeting)
    {
        return targeting == null
            ? new FeatureFlagTargetingInput(false, new List<string>(), new List<string>(), null)
            : new FeatureFlagTargetingInput(
                targeting.InternalOnly,
                targeting.Users?.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
                targeting.Sellers?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
                targeting.PercentageRollout);
    }

    private static FeatureFlagTargetingRule DefaultTargeting() => new(false, new List<string>(), new List<string>(), null);

    private static string SerializeTargeting(FeatureFlagTargetingRule targeting)
    {
        return JsonSerializer.Serialize(targeting);
    }

    private static FeatureFlagTargetingRule DeserializeTargeting(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return DefaultTargeting();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<FeatureFlagTargetingRule>(json);
            return parsed ?? DefaultTargeting();
        }
        catch (JsonException)
        {
            return DefaultTargeting();
        }
    }

    private static int ComputeBucket(string key)
    {
        var normalized = string.IsNullOrWhiteSpace(key) ? "fallback" : key;
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        var value = BitConverter.ToUInt32(hash, 0);
        return (int)(value % 100);
    }

    private static FeatureFlagView MapToView(FeatureFlag entity)
    {
        var environments = entity.Environments
            .OrderBy(e => e.Environment)
            .Select(e => new FeatureFlagEnvironmentView(
                e.Id,
                e.Environment,
                e.Enabled,
                DeserializeTargeting(e.TargetingJson),
                e.CreatedOn,
                e.UpdatedOn))
            .ToList();

        return new FeatureFlagView(
            entity.Id,
            entity.Key,
            entity.Name,
            entity.Description,
            entity.DefaultEnabled,
            environments,
            entity.CreatedOn,
            entity.UpdatedOn);
    }

    private void ApplyEnvironmentChanges(FeatureFlag entity, List<FeatureFlagEnvironmentInput> environments, DateTimeOffset now)
    {
        foreach (var envInput in environments)
        {
            var env = entity.Environments.FirstOrDefault(e => string.Equals(e.Environment, envInput.Environment, StringComparison.OrdinalIgnoreCase));
            if (env == null)
            {
                env = new FeatureFlagEnvironment
                {
                    Environment = envInput.Environment,
                    CreatedOn = now
                };
                entity.Environments.Add(env);
            }

            env.Enabled = envInput.Enabled;
            env.TargetingJson = SerializeTargeting(new FeatureFlagTargetingRule(
                envInput.Targeting.InternalOnly,
                envInput.Targeting.Users ?? new List<string>(),
                envInput.Targeting.Sellers ?? new List<string>(),
                envInput.Targeting.PercentageRollout));
            env.UpdatedOn = now;
        }
    }

    private List<FeatureFlagView> GetCachedFlags()
    {
        lock (_cacheLock)
        {
            return _cachedFlags.ToList();
        }
    }

    private void UpdateCache(List<FeatureFlagView> flags)
    {
        lock (_cacheLock)
        {
            _cachedFlags = flags;
        }
    }

    private void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedFlags = new List<FeatureFlagView>();
        }
    }

    private async Task RecordAuditAsync(
        int flagId,
        string action,
        string actorId,
        string? actorName,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _dbContext.FeatureFlags
                .Include(f => f.Environments)
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == flagId, cancellationToken);

            if (snapshot == null)
            {
                return;
            }

            var audit = new FeatureFlagAudit
            {
                FlagId = flagId,
                Action = action,
                ActorId = actorId,
                ActorName = actorName,
                SnapshotJson = JsonSerializer.Serialize(MapToView(snapshot)),
                ChangedOn = _timeProvider.GetUtcNow()
            };

            _dbContext.FeatureFlagAudits.Add(audit);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to record feature flag audit for {FlagId}", flagId);
        }
    }
}
