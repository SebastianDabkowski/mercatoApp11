using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.Modules.Products.Domain
{
    public class ProductPhotoModerationAudit
    {
        public int Id { get; set; }

        public int PhotoId { get; set; }

        [MaxLength(64)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? Actor { get; set; }

        [MaxLength(512)]
        public string? Reason { get; set; }

        [MaxLength(32)]
        public string FromStatus { get; set; } = string.Empty;

        [MaxLength(32)]
        public string ToStatus { get; set; } = string.Empty;

        public DateTimeOffset CreatedOn { get; set; }
    }
}
