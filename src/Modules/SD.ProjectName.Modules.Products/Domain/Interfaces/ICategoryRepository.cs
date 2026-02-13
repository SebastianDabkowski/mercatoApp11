using SD.ProjectName.Modules.Products.Domain;

namespace SD.ProjectName.Modules.Products.Domain.Interfaces
{
    public interface ICategoryRepository
    {
        Task<List<CategoryModel>> GetAll(bool includeInactive = false);

        Task<CategoryModel?> GetById(int id, bool includeInactive = false);

        Task Add(CategoryModel category);

        Task Update(CategoryModel category);

        Task Delete(CategoryModel category);

        Task<int> GetProductCount(int categoryId);
    }
}
