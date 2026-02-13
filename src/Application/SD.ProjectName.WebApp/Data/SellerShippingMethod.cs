using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Data
{
    public class SellerShippingMethod
    {
        public Guid Id { get; set; }

        [Required]
        [StringLength(450)]
        public string StoreOwnerId { get; set; } = string.Empty;

        [Required]
        [StringLength(128)]
        public string Name { get; set; } = string.Empty;

        [StringLength(1024)]
        public string? Description { get; set; }

        [Range(0, double.MaxValue)]
        public decimal BaseCost { get; set; }

        [StringLength(128)]
        public string? DeliveryEstimate { get; set; }

        [StringLength(256)]
        public string? Availability { get; set; }

        [StringLength(64)]
        public string? ProviderId { get; set; }

        [StringLength(64)]
        public string? ProviderServiceCode { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsDeleted { get; set; }

        public DateTimeOffset CreatedOn { get; set; }

        public DateTimeOffset UpdatedOn { get; set; }
    }
}
