using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services;

public static class ConsentTypes
{
    public const string Newsletter = "Newsletter";
    public const string Profiling = "Profiling";
    public const string ThirdPartySharing = "ThirdPartySharing";

    public static readonly string[] Allowed = [Newsletter, Profiling, ThirdPartySharing];

    public static string Normalize(string? type) =>
        Allowed.FirstOrDefault(t => t.Equals(type, StringComparison.OrdinalIgnoreCase)) ?? Newsletter;

    public static bool IsValid(string? type) =>
        !string.IsNullOrWhiteSpace(type) && Allowed.Any(t => t.Equals(type, StringComparison.OrdinalIgnoreCase));

    public static string GetDisplayName(string? type) => Normalize(type) switch
    {
        Profiling => "Personalized recommendations",
        ThirdPartySharing => "Trusted partner sharing",
        _ => "Newsletter and offers"
    };
}

public class ConsentDefinition
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string ConsentType { get; set; } = ConsentTypes.Newsletter;

    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public bool AllowPreselect { get; set; }

    public bool IsRequired { get; set; }

    public DateTimeOffset CreatedOn { get; set; }

    public ICollection<ConsentVersion> Versions { get; set; } = new List<ConsentVersion>();
}

public class ConsentVersion
{
    public int Id { get; set; }

    public int ConsentDefinitionId { get; set; }

    public ConsentDefinition? ConsentDefinition { get; set; }

    [MaxLength(64)]
    public string VersionTag { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset EffectiveFrom { get; set; }

    public DateTimeOffset CreatedOn { get; set; }
}

public class UserConsentDecision
{
    public int Id { get; set; }

    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public int ConsentVersionId { get; set; }

    public ConsentVersion? ConsentVersion { get; set; }

    public bool Granted { get; set; }

    public DateTimeOffset DecidedOn { get; set; }
}

public record ConsentVersionView(int Id, string VersionTag, DateTimeOffset EffectiveFrom, string Content);

public record ConsentDefinitionView(
    int Id,
    string ConsentType,
    string Title,
    string Description,
    bool AllowPreselect,
    bool IsRequired,
    ConsentVersionView? ActiveVersion,
    ConsentVersionView? UpcomingVersion);

public record UserConsentSnapshot(
    string ConsentType,
    bool Granted,
    DateTimeOffset DecidedOn,
    ConsentVersionView Version);

public record ConsentOperationResult(bool Success, List<string> Errors)
{
    public static ConsentOperationResult Failed(params string[] errors) => new(false, errors.ToList());

    public static ConsentOperationResult Succeeded() => new(true, new List<string>());
}

public interface IConsentService
{
    Task<List<ConsentDefinitionView>> GetActiveConsentsAsync(CancellationToken cancellationToken = default);

    Task<List<UserConsentSnapshot>> GetUserConsentsAsync(string userId, CancellationToken cancellationToken = default);

    Task<ConsentOperationResult> RecordUserConsentsAsync(string userId, IDictionary<string, bool> choices, CancellationToken cancellationToken = default);

    Task<bool> HasActiveConsentAsync(string userId, string consentType, CancellationToken cancellationToken = default);
}

public class ConsentService : IConsentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    private static readonly List<ConsentSeed> Seeds =
    [
        new(ConsentTypes.Newsletter, "Newsletter and offers", "Receive occasional product news and marketplace updates by email.", false, false, "v1", "Stay in the loop with marketplace news, product launches, and seasonal offers sent to your inbox."),
        new(ConsentTypes.Profiling, "Personalized recommendations", "Allow Mercato to tailor recommendations and promotions based on your activity.", false, false, "v1", "We analyze your browsing and purchase activity to personalize recommendations and promotions. You can withdraw this consent at any time."),
        new(ConsentTypes.ThirdPartySharing, "Trusted partner sharing", "Permit sharing limited data with carefully selected partners for co-marketing.", false, false, "v1", "We may share your contact and order preferences with trusted partners for joint offers. We will never sell your data and you can opt out whenever you like.")
    ];

    public ConsentService(ApplicationDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<List<ConsentDefinitionView>> GetActiveConsentsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeedsAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var definitions = await _dbContext.ConsentDefinitions
            .Include(d => d.Versions)
            .AsNoTracking()
            .OrderBy(d => d.Title)
            .ToListAsync(cancellationToken);

        var views = new List<ConsentDefinitionView>();
        foreach (var definition in definitions)
        {
            var active = GetActiveVersion(definition, now);
            if (active == null)
            {
                continue;
            }

            var upcoming = definition.Versions
                .Where(v => v.EffectiveFrom > now)
                .OrderBy(v => v.EffectiveFrom)
                .ThenBy(v => v.Id)
                .FirstOrDefault();

            views.Add(new ConsentDefinitionView(
                definition.Id,
                definition.ConsentType,
                definition.Title,
                definition.Description,
                definition.AllowPreselect,
                definition.IsRequired,
                MapVersion(active),
                upcoming == null ? null : MapVersion(upcoming)));
        }

        return views;
    }

    public async Task<List<UserConsentSnapshot>> GetUserConsentsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new List<UserConsentSnapshot>();
        }

        var decisions = await _dbContext.UserConsentDecisions
            .Include(d => d.ConsentVersion)
            .ThenInclude(v => v!.ConsentDefinition)
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .ToListAsync(cancellationToken);

        var snapshots = new List<UserConsentSnapshot>();
        foreach (var group in decisions.GroupBy(d => d.ConsentVersion!.ConsentDefinition!.ConsentType))
        {
            var latest = group
                .OrderByDescending(d => d.DecidedOn)
                .ThenByDescending(d => d.Id)
                .First();

            var version = latest.ConsentVersion!;
            snapshots.Add(new UserConsentSnapshot(
                version.ConsentDefinition!.ConsentType,
                latest.Granted,
                latest.DecidedOn,
                MapVersion(version)));
        }

        return snapshots;
    }

    public async Task<ConsentOperationResult> RecordUserConsentsAsync(
        string userId,
        IDictionary<string, bool> choices,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ConsentOperationResult.Failed("User id is required.");
        }

        await EnsureSeedsAsync(cancellationToken);

        var normalized = choices
            .Where(c => ConsentTypes.IsValid(c.Key))
            .ToDictionary(c => ConsentTypes.Normalize(c.Key), c => c.Value);

        if (normalized.Count == 0)
        {
            return ConsentOperationResult.Succeeded();
        }

        var now = _timeProvider.GetUtcNow();

        var definitions = await _dbContext.ConsentDefinitions
            .Include(d => d.Versions)
            .Where(d => normalized.Keys.Contains(d.ConsentType))
            .ToListAsync(cancellationToken);

        var errors = new List<string>();

        var latestDecisions = await _dbContext.UserConsentDecisions
            .Include(d => d.ConsentVersion)
            .ThenInclude(v => v!.ConsentDefinition)
            .Where(d => d.UserId == userId && normalized.Keys.Contains(d.ConsentVersion!.ConsentDefinition!.ConsentType))
            .ToListAsync(cancellationToken);

        foreach (var definition in definitions)
        {
            var activeVersion = GetActiveVersion(definition, now);
            if (activeVersion == null)
            {
                errors.Add($"Active consent text missing for {definition.Title}.");
                continue;
            }

            var desired = normalized[definition.ConsentType];
            var latest = latestDecisions
                .Where(d => d.ConsentVersion!.ConsentDefinition!.ConsentType == definition.ConsentType)
                .OrderByDescending(d => d.DecidedOn)
                .ThenByDescending(d => d.Id)
                .FirstOrDefault();

            if (latest != null &&
                latest.Granted == desired &&
                latest.ConsentVersionId == activeVersion.Id)
            {
                continue;
            }

            _dbContext.UserConsentDecisions.Add(new UserConsentDecision
            {
                UserId = userId,
                ConsentVersionId = activeVersion.Id,
                Granted = desired,
                DecidedOn = now
            });
        }

        if (errors.Count > 0)
        {
            return ConsentOperationResult.Failed(errors.ToArray());
        }

        if (_dbContext.ChangeTracker.HasChanges())
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return ConsentOperationResult.Succeeded();
    }

    public async Task<bool> HasActiveConsentAsync(string userId, string consentType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || !ConsentTypes.IsValid(consentType))
        {
            return false;
        }

        await EnsureSeedsAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var normalizedType = ConsentTypes.Normalize(consentType);

        var activeVersion = await _dbContext.ConsentVersions
            .Include(v => v.ConsentDefinition)
            .AsNoTracking()
            .Where(v => v.ConsentDefinition!.ConsentType == normalizedType && v.EffectiveFrom <= now)
            .OrderByDescending(v => v.EffectiveFrom)
            .ThenByDescending(v => v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeVersion == null)
        {
            return false;
        }

        var decision = await _dbContext.UserConsentDecisions
            .Where(d => d.UserId == userId && d.ConsentVersion!.ConsentDefinition!.ConsentType == normalizedType)
            .OrderByDescending(d => d.DecidedOn)
            .ThenByDescending(d => d.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return decision != null && decision.Granted && decision.ConsentVersionId == activeVersion.Id;
    }

    private async Task EnsureSeedsAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        foreach (var seed in Seeds)
        {
            var definition = await _dbContext.ConsentDefinitions
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.ConsentType == seed.ConsentType, cancellationToken);

            if (definition == null)
            {
                definition = new ConsentDefinition
                {
                    ConsentType = seed.ConsentType,
                    Title = seed.Title,
                    Description = seed.Description,
                    AllowPreselect = seed.AllowPreselect,
                    IsRequired = seed.IsRequired,
                    CreatedOn = now
                };
                definition.Versions.Add(new ConsentVersion
                {
                    VersionTag = seed.VersionTag,
                    Content = seed.Content,
                    EffectiveFrom = now,
                    CreatedOn = now
                });
                _dbContext.ConsentDefinitions.Add(definition);
            }
            else
            {
                if (!definition.Versions.Any())
                {
                    definition.Versions.Add(new ConsentVersion
                    {
                        VersionTag = seed.VersionTag,
                        Content = seed.Content,
                        EffectiveFrom = now,
                        CreatedOn = now
                    });
                }

                if (string.IsNullOrWhiteSpace(definition.Title))
                {
                    definition.Title = seed.Title;
                }

                if (string.IsNullOrWhiteSpace(definition.Description))
                {
                    definition.Description = seed.Description;
                }
            }
        }

        if (_dbContext.ChangeTracker.HasChanges())
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static ConsentVersion? GetActiveVersion(ConsentDefinition definition, DateTimeOffset now) =>
        definition.Versions
            .Where(v => v.EffectiveFrom <= now)
            .OrderByDescending(v => v.EffectiveFrom)
            .ThenByDescending(v => v.Id)
            .FirstOrDefault();

    private static ConsentVersionView MapVersion(ConsentVersion version) =>
        new(version.Id, version.VersionTag, version.EffectiveFrom, version.Content);

    private record ConsentSeed(
        string ConsentType,
        string Title,
        string Description,
        bool AllowPreselect,
        bool IsRequired,
        string VersionTag,
        string Content);
}

public class MarketingEmailService
{
    private readonly IConsentService _consents;
    private readonly IEmailSender _emailSender;

    public MarketingEmailService(IConsentService consents, IEmailSender emailSender)
    {
        _consents = consents;
        _emailSender = emailSender;
    }

    public async Task<bool> SendAsync(string userId, string email, string subject, string bodyHtml, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var allowed = await _consents.HasActiveConsentAsync(userId, ConsentTypes.Newsletter, cancellationToken);
        if (!allowed)
        {
            return false;
        }

        await _emailSender.SendEmailAsync(email, subject, bodyHtml);
        return true;
    }
}
