using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.Modules.Products.Domain
{
    public class ProductModel
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Range(0.01, double.MaxValue)]
        public decimal Price { get; set; }

        [Range(0, int.MaxValue)]
        public int Stock { get; set; }

        [Required]
        [MaxLength(100)]
        public string Category { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(32)]
        public string WorkflowState { get; set; } = ProductWorkflowStates.Draft;

        [Required]
        public string SellerId { get; set; } = string.Empty;
    }
}
