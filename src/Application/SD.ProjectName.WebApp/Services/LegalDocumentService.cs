using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Data;

namespace SD.ProjectName.WebApp.Services
{
    public static class LegalDocumentTypes
    {
        public const string TermsOfService = "TermsOfService";
        public const string PrivacyPolicy = "PrivacyPolicy";
        public const string CookiePolicy = "CookiePolicy";
        public const string SellerAgreement = "SellerAgreement";

        public static readonly string[] Allowed =
        [
            TermsOfService,
            PrivacyPolicy,
            CookiePolicy,
            SellerAgreement
        ];

        public static string Normalize(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return TermsOfService;
            }

            return Allowed.FirstOrDefault(t => t.Equals(type, StringComparison.OrdinalIgnoreCase))
                   ?? TermsOfService;
        }

        public static bool IsValid(string? type) =>
            !string.IsNullOrWhiteSpace(type) &&
            Allowed.Any(t => t.Equals(type, StringComparison.OrdinalIgnoreCase));

        public static string GetDisplayName(string? type) => Normalize(type) switch
        {
            PrivacyPolicy => "Privacy Policy",
            CookiePolicy => "Cookie Policy",
            SellerAgreement => "Seller Agreement",
            _ => "Terms of Service"
        };
    }

    public class LegalDocumentVersion
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string DocumentType { get; set; } = LegalDocumentTypes.TermsOfService;

        [MaxLength(64)]
        public string VersionTag { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? Title { get; set; }

        public string Content { get; set; } = string.Empty;

        public DateTimeOffset EffectiveFrom { get; set; }

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
    }

    public record LegalDocumentInput
    {
        public int? Id { get; init; }

        [Required]
        [MaxLength(64)]
        public string DocumentType { get; init; } = LegalDocumentTypes.TermsOfService;

        [MaxLength(64)]
        public string? VersionTag { get; init; }

        [MaxLength(256)]
        public string? Title { get; init; }

        [Required]
        public string Content { get; init; } = string.Empty;

        [Required]
        public DateTimeOffset EffectiveFrom { get; init; }
    }

    public record LegalDocumentOperationResult(bool Success, LegalDocumentVersion? Version, List<string> Errors)
    {
        public static LegalDocumentOperationResult Failed(params string[] errors) => new(false, null, errors.ToList());
        public static LegalDocumentOperationResult Succeeded(LegalDocumentVersion version) => new(true, version, new List<string>());
    }

    public interface ILegalDocumentService
    {
        Task<LegalDocumentOperationResult> SaveAsync(LegalDocumentInput input, string actorId, string? actorName, CancellationToken cancellationToken = default);
        Task<List<LegalDocumentVersion>> GetVersionsAsync(string documentType, CancellationToken cancellationToken = default);
        Task<LegalDocumentVersion?> GetVersionAsync(int id, CancellationToken cancellationToken = default);
        Task<LegalDocumentVersion?> GetActiveVersionAsync(string documentType, DateTimeOffset asOf, CancellationToken cancellationToken = default);
        Task<LegalDocumentVersion?> GetUpcomingVersionAsync(string documentType, DateTimeOffset asOf, CancellationToken cancellationToken = default);
    }

    public class LegalDocumentService : ILegalDocumentService
    {
        private readonly ApplicationDbContext _dbContext;

        public LegalDocumentService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<LegalDocumentOperationResult> SaveAsync(LegalDocumentInput input, string actorId, string? actorName, CancellationToken cancellationToken = default)
        {
            var errors = ValidateInput(input);
            if (errors.Count > 0)
            {
                return LegalDocumentOperationResult.Failed(errors.ToArray());
            }

            var normalizedType = LegalDocumentTypes.Normalize(input.DocumentType);
            var versionTag = BuildVersionTag(input.VersionTag);

            var duplicateTag = await _dbContext.LegalDocumentVersions
                .AsNoTracking()
                .AnyAsync(d => d.DocumentType == normalizedType && d.VersionTag == versionTag && d.Id != input.Id, cancellationToken);
            if (duplicateTag)
            {
                errors.Add($"Version tag '{versionTag}' already exists for {LegalDocumentTypes.GetDisplayName(normalizedType)}.");
                return LegalDocumentOperationResult.Failed(errors.ToArray());
            }

            LegalDocumentVersion? version = null;
            if (input.Id.HasValue)
            {
                version = await _dbContext.LegalDocumentVersions.FirstOrDefaultAsync(d => d.Id == input.Id.Value, cancellationToken);
            }

            if (version == null)
            {
                version = new LegalDocumentVersion
                {
                    DocumentType = normalizedType,
                    CreatedBy = actorId,
                    CreatedByName = actorName,
                    CreatedOn = DateTimeOffset.UtcNow
                };
                _dbContext.LegalDocumentVersions.Add(version);
            }

            version.DocumentType = normalizedType;
            version.VersionTag = versionTag;
            version.Title = input.Title?.Trim();
            version.Content = input.Content?.Trim() ?? string.Empty;
            version.EffectiveFrom = input.EffectiveFrom;
            version.UpdatedBy = actorId;
            version.UpdatedByName = actorName;
            version.UpdatedOn = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            return LegalDocumentOperationResult.Succeeded(version);
        }

        public async Task<List<LegalDocumentVersion>> GetVersionsAsync(string documentType, CancellationToken cancellationToken = default)
        {
            var normalizedType = LegalDocumentTypes.Normalize(documentType);
            return await _dbContext.LegalDocumentVersions
                .AsNoTracking()
                .Where(d => d.DocumentType == normalizedType)
                .OrderByDescending(d => d.EffectiveFrom)
                .ThenByDescending(d => d.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<LegalDocumentVersion?> GetVersionAsync(int id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.LegalDocumentVersions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        }

        public async Task<LegalDocumentVersion?> GetActiveVersionAsync(string documentType, DateTimeOffset asOf, CancellationToken cancellationToken = default)
        {
            var normalizedType = LegalDocumentTypes.Normalize(documentType);
            return await _dbContext.LegalDocumentVersions.AsNoTracking()
                .Where(d => d.DocumentType == normalizedType && d.EffectiveFrom <= asOf)
                .OrderByDescending(d => d.EffectiveFrom)
                .ThenByDescending(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<LegalDocumentVersion?> GetUpcomingVersionAsync(string documentType, DateTimeOffset asOf, CancellationToken cancellationToken = default)
        {
            var normalizedType = LegalDocumentTypes.Normalize(documentType);
            return await _dbContext.LegalDocumentVersions.AsNoTracking()
                .Where(d => d.DocumentType == normalizedType && d.EffectiveFrom > asOf)
                .OrderBy(d => d.EffectiveFrom)
                .ThenBy(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private static List<string> ValidateInput(LegalDocumentInput input)
        {
            var errors = new List<string>();
            if (!LegalDocumentTypes.IsValid(input.DocumentType))
            {
                errors.Add("Choose a valid document type.");
            }

            if (input.EffectiveFrom == default)
            {
                errors.Add("Effective date is required.");
            }

            if (string.IsNullOrWhiteSpace(input.Content))
            {
                errors.Add("Content is required.");
            }

            return errors;
        }

        private static string BuildVersionTag(string? tag)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                return tag.Trim();
            }

            return $"v{DateTime.UtcNow:yyyyMMddHHmmss}";
        }
    }
}
