using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.Modules.Products.Domain
{
    public class CategoryModel
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        public string FullPath { get; set; } = string.Empty;

        public int? ParentId { get; set; }

        public CategoryModel? Parent { get; set; }

        public List<CategoryModel> Children { get; set; } = new();

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
