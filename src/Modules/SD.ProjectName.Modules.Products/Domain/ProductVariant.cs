using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.Modules.Products.Domain
{
    public class ProductVariant
    {
        [MaxLength(100)]
        public string Sku { get; set; } = string.Empty;

        [Range(0.01, double.MaxValue)]
        public decimal Price { get; set; }

        [Range(0, int.MaxValue)]
        public int Stock { get; set; }

        public Dictionary<string, string> Attributes { get; set; } = new();

        [MaxLength(500)]
        public string? ImageUrl { get; set; }
    }
}

