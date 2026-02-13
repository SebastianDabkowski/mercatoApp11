using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.Modules.Products.Domain
{
    public static class ProductImportJobStatus
    {
        public const string PendingConfirmation = "pending_confirmation";
        public const string Queued = "queued";
        public const string Processing = "processing";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    public class ProductImportJob
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(200)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string Status { get; set; } = ProductImportJobStatus.PendingConfirmation;

        [Required]
        public string SellerId { get; set; } = string.Empty;

        public int TotalRows { get; set; }

        public int CreatedCount { get; set; }

        public int UpdatedCount { get; set; }

        public int FailedCount { get; set; }

        [MaxLength(4000)]
        public string? Summary { get; set; }

        public string? ErrorReport { get; set; }

        public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? CompletedOn { get; set; }

        public byte[]? FileContent { get; set; }

        [MaxLength(128)]
        public string? ContentType { get; set; }

        [MaxLength(32)]
        public string TemplateVersion { get; set; } = "v1";
    }
}
