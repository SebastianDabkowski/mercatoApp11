using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.Modules.Products.Domain
{
    public class ProductPhoto
    {
        public int Id { get; set; }

        public int ProductId { get; set; }

        [MaxLength(500)]
        public string Url { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? ThumbnailUrl { get; set; }

        public bool IsMain { get; set; }

        [MaxLength(32)]
        public string Status { get; set; } = ProductPhotoStatuses.Approved;

        [MaxLength(256)]
        public string? FlaggedBy { get; set; }

        [MaxLength(512)]
        public string? FlaggedReason { get; set; }

        public DateTimeOffset CreatedOn { get; set; }

        public DateTimeOffset? FlaggedOn { get; set; }

        [MaxLength(256)]
        public string? ReviewedBy { get; set; }

        public DateTimeOffset? ReviewedOn { get; set; }

        [MaxLength(512)]
        public string? ModerationNote { get; set; }
    }
}
