using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.Modules.Products.Domain
{
    public class CategoryAttributeDefinition
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string Type { get; set; } = CategoryAttributeTypes.Text;

        public bool IsRequired { get; set; }

        public bool IsDeprecated { get; set; }

        [MaxLength(1000)]
        public string? Options { get; set; }

        public List<CategoryAttributeUsage> Usages { get; set; } = new();
    }
}
