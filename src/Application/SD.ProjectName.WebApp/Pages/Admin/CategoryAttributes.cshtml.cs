using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Admin
{
    [Authorize(Policy = Permissions.AdminCatalog)]
    public class CategoryAttributesModel : PageModel
    {
        private readonly ManageCategories _categories;
        private readonly ManageCategoryAttributes _attributes;

        public CategoryAttributesModel(ManageCategories categories, ManageCategoryAttributes attributes)
        {
            _categories = categories;
            _attributes = attributes;
        }

        [BindProperty]
        public AddAttributeInput AddInput { get; set; } = new();

        [BindProperty]
        public UpdateAttributeInput UpdateInput { get; set; } = new();

        [BindProperty]
        public LinkAttributeInput LinkInput { get; set; } = new();

        [BindProperty]
        public ToggleAttributeInput ToggleInput { get; set; } = new();

        public CategoryModel? Category { get; private set; }
        public List<CategoryAttributeDefinition> Attributes { get; private set; } = new();
        public List<SelectListItem> LinkableAttributes { get; private set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            return await LoadAsync(id);
        }

        public async Task<IActionResult> OnPostAddAsync(int id)
        {
            if (AddInput.Type == CategoryAttributeTypes.List && string.IsNullOrWhiteSpace(AddInput.Options))
            {
                ModelState.AddModelError(nameof(AddInput.Options), "Provide at least one option for list attributes.");
            }

            if (!ModelState.IsValid)
            {
                return await LoadAsync(id);
            }

            var result = await _attributes.AddOrLinkAsync(id, AddInput.Name, AddInput.Type, AddInput.IsRequired, AddInput.Options);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to add attribute.");
                return await LoadAsync(id);
            }

            StatusMessage = $"Attribute '{AddInput.Name}' added.";
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostUpdateAsync(int id)
        {
            if (UpdateInput.Type == CategoryAttributeTypes.List && string.IsNullOrWhiteSpace(UpdateInput.Options))
            {
                ModelState.AddModelError(nameof(UpdateInput.Options), "Provide at least one option for list attributes.");
            }

            if (!ModelState.IsValid)
            {
                return await LoadAsync(id);
            }

            var result = await _attributes.UpdateDefinitionAsync(UpdateInput.Id, UpdateInput.Name, UpdateInput.Type, UpdateInput.IsRequired, UpdateInput.Options);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to update attribute.");
                return await LoadAsync(id);
            }

            StatusMessage = "Attribute updated.";
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostLinkAsync(int id)
        {
            if (!ModelState.IsValid)
            {
                return await LoadAsync(id);
            }

            var result = await _attributes.LinkExistingAsync(LinkInput.DefinitionId, id);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to link attribute.");
                return await LoadAsync(id);
            }

            StatusMessage = "Attribute linked to this category.";
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostToggleAsync(int id)
        {
            var result = await _attributes.SetDeprecatedAsync(ToggleInput.Id, ToggleInput.IsDeprecated);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to update attribute.");
                return await LoadAsync(id);
            }

            StatusMessage = ToggleInput.IsDeprecated ? "Attribute marked as deprecated." : "Attribute reactivated.";
            return RedirectToPage(new { id });
        }

        private async Task<IActionResult> LoadAsync(int id)
        {
            Category = await _categories.GetById(id, includeInactive: true);
            if (Category == null)
            {
                return NotFound();
            }

            Attributes = await _attributes.GetForCategoryAsync(id, includeDeprecated: true);
            LinkableAttributes = (await _attributes.GetLinkableDefinitionsAsync(id))
                .Select(a => new SelectListItem
                {
                    Value = a.Id.ToString(),
                    Text = $"{a.Name} ({a.Type}{(a.IsRequired ? ", required" : string.Empty)})"
                })
                .ToList();

            return Page();
        }

        public class AddAttributeInput
        {
            [Required]
            [MaxLength(120)]
            public string Name { get; set; } = string.Empty;

            [Required]
            public string Type { get; set; } = CategoryAttributeTypes.Text;

            public bool IsRequired { get; set; }

            [Display(Name = "Options (comma separated)")]
            [MaxLength(1000)]
            public string? Options { get; set; }
        }

        public class UpdateAttributeInput : AddAttributeInput
        {
            [Required]
            public int Id { get; set; }
        }

        public class LinkAttributeInput
        {
            [Required]
            public int DefinitionId { get; set; }
        }

        public class ToggleAttributeInput
        {
            [Required]
            public int Id { get; set; }

            public bool IsDeprecated { get; set; }
        }
    }
}
