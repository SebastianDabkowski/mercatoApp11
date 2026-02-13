using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller.Products
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class AddModel : PageModel
    {
        private readonly CreateProduct _createProduct;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ManageCategories _categories;
        private readonly IProductRepository _productRepository;

        public AddModel(CreateProduct createProduct, UserManager<ApplicationUser> userManager, ManageCategories categories, IProductRepository productRepository)
        {
            _createProduct = createProduct;
            _userManager = userManager;
            _categories = categories;
            _productRepository = productRepository;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public List<SelectListItem> CategoryOptions { get; private set; } = new();

        public async Task<IActionResult> OnGet()
        {
            await LoadCategoriesAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await LoadCategoriesAsync();

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (!Input.CategoryId.HasValue)
            {
                ModelState.AddModelError(nameof(Input.CategoryId), "Select a category.");
                return Page();
            }

            var category = await _categories.GetById(Input.CategoryId.Value);
            if (category == null)
            {
                ModelState.AddModelError(nameof(Input.CategoryId), "Select a valid active category.");
                return Page();
            }

            var existingSku = await _productRepository.GetBySku(user.Id, Input.MerchantSku.Trim(), includeDrafts: true);
            if (existingSku != null)
            {
                ModelState.AddModelError(nameof(Input.MerchantSku), "This SKU is already used by another product.");
                return Page();
            }

            var product = new ProductModel
            {
                Title = Input.Title.Trim(),
                MerchantSku = Input.MerchantSku.Trim(),
                Price = Input.Price,
                Stock = Input.Stock,
                Category = category.FullPath,
                CategoryId = category.Id,
                Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
                MainImageUrl = string.IsNullOrWhiteSpace(Input.MainImageUrl) ? null : Input.MainImageUrl.Trim(),
                GalleryImageUrls = string.IsNullOrWhiteSpace(Input.GalleryImageUrls) ? null : Input.GalleryImageUrls.Trim(),
                WeightKg = Input.WeightKg,
                LengthCm = Input.LengthCm,
                WidthCm = Input.WidthCm,
                HeightCm = Input.HeightCm,
                ShippingMethods = string.IsNullOrWhiteSpace(Input.ShippingMethods) ? null : Input.ShippingMethods.Trim(),
                WorkflowState = ProductWorkflowStates.Draft,
                SellerId = user.Id
            };

            await _createProduct.CreateAsync(product);

            TempData["StatusMessage"] = "Product saved as draft.";
            return RedirectToPage("List");
        }

        public class InputModel
        {
            [Required]
            [MaxLength(200)]
            [Display(Name = "Title")]
            public string Title { get; set; } = string.Empty;

            [Required]
            [MaxLength(100)]
            [Display(Name = "Merchant SKU")]
            public string MerchantSku { get; set; } = string.Empty;

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

        private async Task LoadCategoriesAsync()
        {
            var tree = await _categories.GetTree();
            CategoryOptions = tree
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.FullPath,
                    Selected = Input.CategoryId.HasValue && Input.CategoryId.Value == c.Id
                })
                .ToList();
        }
    }
}
