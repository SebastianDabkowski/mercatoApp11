using System;
using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.Modules.Products.Domain
{
    public static class ProductExportJobStatus
    {
        public const string Pending = "pending";
        public const string Queued = "queued";
        public const string Processing = "processing";
        public const string Completed = "completed";
        public const string Failed = "failed";

        public static bool IsTerminal(string status) =>
            status == Completed || status == Failed;
    }

    public class ProductExportJob
    {
        public Guid Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string SellerId { get; set; } = string.Empty;

        [Required]
        [MaxLength(16)]
        public string Format { get; set; } = "csv";

        [Required]
        [MaxLength(64)]
        public string Status { get; set; } = ProductExportJobStatus.Pending;

        [MaxLength(200)]
        public string? Search { get; set; }

        [MaxLength(32)]
        public string? WorkflowState { get; set; }

        public bool UseFilters { get; set; }

        public int TotalProducts { get; set; }

        [MaxLength(4000)]
        public string? Summary { get; set; }

        [MaxLength(200)]
        public string FileName { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ContentType { get; set; }

        public byte[]? FileContent { get; set; }

        public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? CompletedOn { get; set; }

        public string? Error { get; set; }
    }
}
