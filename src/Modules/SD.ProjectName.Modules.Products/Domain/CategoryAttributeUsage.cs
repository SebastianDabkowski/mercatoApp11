namespace SD.ProjectName.Modules.Products.Domain
{
    public class CategoryAttributeUsage
    {
        public int Id { get; set; }

        public int CategoryId { get; set; }

        public CategoryModel? Category { get; set; }

        public int DefinitionId { get; set; }

        public CategoryAttributeDefinition? Definition { get; set; }
    }
}
