using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Admin
{
    [Authorize(Roles = AccountTypes.Admin)]
    public class CategoriesModel : PageModel
    {
        private readonly ManageCategories _categories;

        public CategoriesModel(ManageCategories categories)
        {
            _categories = categories;
        }

        public IReadOnlyList<CategoryNode> Categories { get; private set; } = Array.Empty<CategoryNode>();

        public List<SelectListItem> ParentOptions { get; private set; } = new();

        [BindProperty]
        public CreateInputModel CreateInput { get; set; } = new();

        [BindProperty]
        public RenameInputModel RenameInput { get; set; } = new();

        [BindProperty]
        public OrderInputModel OrderInput { get; set; } = new();

        [BindProperty]
        public ToggleInputModel ToggleInput { get; set; } = new();

        [BindProperty]
        public DeleteInputModel DeleteInput { get; set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task OnGet()
        {
            await LoadAsync();
        }

        public async Task<IActionResult> OnPostCreateAsync()
        {
            ModelState.ClearValidationState(nameof(CreateInput));
            if (!TryValidateModel(CreateInput, nameof(CreateInput)))
            {
                await LoadAsync();
                return Page();
            }

            var slug = string.IsNullOrWhiteSpace(CreateInput.Slug) ? null : CreateInput.Slug;
            var description = string.IsNullOrWhiteSpace(CreateInput.Description) ? null : CreateInput.Description;
            var (result, _) = await _categories.CreateAsync(CreateInput.Name, CreateInput.ParentId, description, slug);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to create category.");
            }
            else
            {
                StatusMessage = "Category created.";
            }

            await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostRenameAsync()
        {
            ModelState.ClearValidationState(nameof(RenameInput));
            if (!TryValidateModel(RenameInput, nameof(RenameInput)))
            {
                await LoadAsync();
                return Page();
            }

            var slug = string.IsNullOrWhiteSpace(RenameInput.Slug) ? null : RenameInput.Slug;
            var description = string.IsNullOrWhiteSpace(RenameInput.Description) ? null : RenameInput.Description;
            var result = await _categories.RenameAsync(RenameInput.Id, RenameInput.Name, slug, RenameInput.ParentId, description);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to rename category.");
            }
            else
            {
                StatusMessage = "Category renamed.";
            }

            await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostOrderAsync()
        {
            ModelState.ClearValidationState(nameof(OrderInput));
            if (!TryValidateModel(OrderInput, nameof(OrderInput)))
            {
                await LoadAsync();
                return Page();
            }

            var result = await _categories.UpdateSortOrderAsync(OrderInput.Id, OrderInput.SortOrder);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to reorder category.");
            }
            else
            {
                StatusMessage = "Category order updated.";
            }

            await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostToggleAsync()
        {
            ModelState.ClearValidationState(nameof(ToggleInput));
            if (!TryValidateModel(ToggleInput, nameof(ToggleInput)))
            {
                await LoadAsync();
                return Page();
            }

            var result = await _categories.SetActiveAsync(ToggleInput.Id, ToggleInput.IsActive);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to update category.");
            }
            else
            {
                StatusMessage = ToggleInput.IsActive ? "Category activated." : "Category deactivated.";
            }

            await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync()
        {
            ModelState.ClearValidationState(nameof(DeleteInput));
            if (!TryValidateModel(DeleteInput, nameof(DeleteInput)))
            {
                await LoadAsync();
                return Page();
            }

            var result = await _categories.DeleteAsync(DeleteInput.Id, DeleteInput.ReassignCategoryId);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to delete category.");
            }
            else
            {
                StatusMessage = "Category deleted.";
            }

            await LoadAsync();
            return Page();
        }

        private async Task LoadAsync()
        {
            Categories = await _categories.GetTree(includeInactive: true);
            ParentOptions = new List<SelectListItem>
            {
                new() { Value = string.Empty, Text = "(no parent)" }
            };

            ParentOptions.AddRange(Categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.FullPath
            }));
        }

        public class CreateInputModel
        {
            [Required]
            [MaxLength(120)]
            [Display(Name = "Name")]
            public string Name { get; set; } = string.Empty;

            [MaxLength(160)]
            [Display(Name = "Slug (optional)")]
            public string? Slug { get; set; }

            [MaxLength(512)]
            [Display(Name = "Description (optional)")]
            public string? Description { get; set; }

            [Display(Name = "Parent category")]
            public int? ParentId { get; set; }
        }

        public class RenameInputModel
        {
            [Required]
            public int Id { get; set; }

            [Required]
            [MaxLength(120)]
            [Display(Name = "New name")]
            public string Name { get; set; } = string.Empty;

            [MaxLength(160)]
            [Display(Name = "Slug")]
            public string? Slug { get; set; }

            [Display(Name = "Parent category")]
            public int? ParentId { get; set; }

            [MaxLength(512)]
            [Display(Name = "Description (optional)")]
            public string? Description { get; set; }
        }

        public class OrderInputModel
        {
            [Required]
            public int Id { get; set; }

            [Display(Name = "Sort order")]
            public int SortOrder { get; set; }
        }

        public class ToggleInputModel
        {
            [Required]
            public int Id { get; set; }

            public bool IsActive { get; set; }
        }

        public class DeleteInputModel
        {
            [Required]
            public int Id { get; set; }

            [Display(Name = "Reassign products to")]
            public int? ReassignCategoryId { get; set; }
        }
    }
}
