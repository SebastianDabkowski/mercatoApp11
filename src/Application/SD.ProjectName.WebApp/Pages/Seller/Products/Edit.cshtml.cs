using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller.Products
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class EditModel : PageModel
    {
        private readonly GetProducts _getProducts;
        private readonly UpdateProduct _updateProduct;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ManageCategories _categories;
        private readonly ILogger<EditModel> _logger;

        public EditModel(
            GetProducts getProducts,
            UpdateProduct updateProduct,
            UserManager<ApplicationUser> userManager,
            ManageCategories categories,
            ILogger<EditModel> logger)
        {
            _getProducts = getProducts;
            _updateProduct = updateProduct;
            _userManager = userManager;
            _categories = categories;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public List<SelectListItem> CategoryOptions { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var product = await _getProducts.GetById(id, includeDrafts: true);
            if (product == null)
            {
                return NotFound();
            }

            if (product.SellerId != user.Id)
            {
                return Forbid();
            }

            Input = new InputModel
            {
                Id = product.Id,
                Title = product.Title,
                Price = product.Price,
                Stock = product.Stock,
                CategoryId = product.CategoryId,
                Description = product.Description,
                MainImageUrl = product.MainImageUrl,
                GalleryImageUrls = product.GalleryImageUrls,
                WeightKg = product.WeightKg,
                LengthCm = product.LengthCm,
                WidthCm = product.WidthCm,
                HeightCm = product.HeightCm,
                ShippingMethods = product.ShippingMethods
            };

            await LoadCategoriesAsync(product.CategoryId);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await LoadCategoriesAsync(Input.CategoryId);

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var product = await _getProducts.GetById(Input.Id, includeDrafts: true);
            if (product == null)
            {
                return NotFound();
            }

            if (product.SellerId != user.Id)
            {
                return Forbid();
            }

            if (!Input.CategoryId.HasValue)
            {
                ModelState.AddModelError(nameof(Input.CategoryId), "Select a category.");
                return Page();
            }

            var category = await _categories.GetById(Input.CategoryId.Value, includeInactive: true);
            if (category == null || (!category.IsActive && product.CategoryId != category.Id))
            {
                ModelState.AddModelError(nameof(Input.CategoryId), "Select a valid active category.");
                return Page();
            }

            product.Title = Input.Title.Trim();
            product.Price = Input.Price;
            product.Stock = Input.Stock;
            product.CategoryId = category.Id;
            product.Category = category.FullPath;
            product.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();
            product.MainImageUrl = string.IsNullOrWhiteSpace(Input.MainImageUrl) ? null : Input.MainImageUrl.Trim();
            product.GalleryImageUrls = string.IsNullOrWhiteSpace(Input.GalleryImageUrls) ? null : Input.GalleryImageUrls.Trim();
            product.WeightKg = Input.WeightKg;
            product.LengthCm = Input.LengthCm;
            product.WidthCm = Input.WidthCm;
            product.HeightCm = Input.HeightCm;
            product.ShippingMethods = string.IsNullOrWhiteSpace(Input.ShippingMethods) ? null : Input.ShippingMethods.Trim();

            await _updateProduct.UpdateAsync(product);
            _logger.LogInformation("Product {ProductId} updated by seller {SellerId}", product.Id, user.Id);

            TempData["StatusMessage"] = "Product updated.";
            return RedirectToPage("List");
        }

        public class InputModel
        {
            [Required]
            public int Id { get; set; }

            [Required]
            [MaxLength(200)]
            [Display(Name = "Title")]
            public string Title { get; set; } = string.Empty;

            [Required]
            [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than zero.")]
            public decimal Price { get; set; }

            [Required]
            [Range(0, int.MaxValue)]
            public int Stock { get; set; }

            [Required(ErrorMessage = "Category is required.")]
            [Display(Name = "Category")]
            public int? CategoryId { get; set; }

            [MaxLength(1000)]
            public string? Description { get; set; }

            [MaxLength(500)]
            [Display(Name = "Main image URL")]
            [Url(ErrorMessage = "Provide a valid image URL.")]
            public string? MainImageUrl { get; set; }

            [MaxLength(2000)]
            [Display(Name = "Gallery image URLs (comma-separated)")]
            public string? GalleryImageUrls { get; set; }

            [Range(0, double.MaxValue)]
            [Display(Name = "Weight (kg)")]
            public decimal? WeightKg { get; set; }

            [Range(0, double.MaxValue)]
            [Display(Name = "Length (cm)")]
            public decimal? LengthCm { get; set; }

            [Range(0, double.MaxValue)]
            [Display(Name = "Width (cm)")]
            public decimal? WidthCm { get; set; }

            [Range(0, double.MaxValue)]
            [Display(Name = "Height (cm)")]
            public decimal? HeightCm { get; set; }

            [MaxLength(200)]
            [Display(Name = "Shipping methods")]
            public string? ShippingMethods { get; set; }
        }

        private async Task LoadCategoriesAsync(int? selectedCategoryId)
        {
            var tree = await _categories.GetTree();
            CategoryOptions = tree
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.FullPath,
                    Selected = selectedCategoryId.HasValue && selectedCategoryId.Value == c.Id
                })
                .ToList();

            if (selectedCategoryId.HasValue && CategoryOptions.All(o => o.Value != selectedCategoryId.Value.ToString()))
            {
                var inactive = await _categories.GetById(selectedCategoryId.Value, includeInactive: true);
                if (inactive != null)
                {
                    CategoryOptions.Add(new SelectListItem
                    {
                        Value = inactive.Id.ToString(),
                        Text = $"{inactive.FullPath} (inactive)",
                        Selected = true
                    });
                }
            }
        }
    }
}
