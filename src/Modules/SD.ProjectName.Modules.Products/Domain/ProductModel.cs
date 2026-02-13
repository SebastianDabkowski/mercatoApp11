using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace SD.ProjectName.Modules.Products.Domain
{
    public class ProductModel
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string MerchantSku { get; set; } = string.Empty;

        [Range(0.01, double.MaxValue)]
        public decimal Price { get; set; }

        [Range(0, int.MaxValue)]
        public int Stock { get; set; }

        [Required]
        [MaxLength(256)]
        public string Category { get; set; } = string.Empty;

        public int? CategoryId { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        [MaxLength(500)]
        public string? MainImageUrl { get; set; }

        [MaxLength(2000)]
        public string? GalleryImageUrls { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? WeightKg { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? LengthCm { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? WidthCm { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? HeightCm { get; set; }

        [MaxLength(200)]
        public string? ShippingMethods { get; set; }

        [MaxLength(32)]
        public string Condition { get; set; } = ProductConditions.New;

        public bool HasVariants { get; set; }

        [MaxLength(8000)]
        public string? VariantData { get; set; }

        [NotMapped]
        public List<ProductVariant> Variants
        {
            get => ProductVariantSerializer.Deserialize(VariantData);
            set => VariantData = ProductVariantSerializer.Serialize(value);
        }

        [Required]
        [MaxLength(32)]
        public string WorkflowState { get; set; } = ProductWorkflowStates.Draft;

        [Required]
        public string SellerId { get; set; } = string.Empty;

        public bool IsSellerBlocked { get; set; }
    }
}
