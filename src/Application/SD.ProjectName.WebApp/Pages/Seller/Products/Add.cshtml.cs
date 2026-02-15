using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;
using System.Text.Json;

namespace SD.ProjectName.WebApp.Pages.Seller.Products
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class AddModel : PageModel
    {
        private readonly CreateProduct _createProduct;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ManageCategories _categories;
        private readonly IProductRepository _productRepository;
        private readonly ProductImageService _productImageService;
        private readonly ProductPhotoModerationService _photoModerationService;
        private readonly ManageCategoryAttributes _categoryAttributes;

        public AddModel(
            CreateProduct createProduct,
            UserManager<ApplicationUser> userManager,
            ManageCategories categories,
            IProductRepository productRepository,
            ProductImageService productImageService,
            ProductPhotoModerationService photoModerationService,
            ManageCategoryAttributes categoryAttributes)
        {
            _createProduct = createProduct;
            _userManager = userManager;
            _categories = categories;
            _productRepository = productRepository;
            _productImageService = productImageService;
            _photoModerationService = photoModerationService;
            _categoryAttributes = categoryAttributes;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public List<SelectListItem> CategoryOptions { get; private set; } = new();
        public List<string> ImagePreviews { get; private set; } = new();
        public string AttributeTemplatesJson { get; private set; } = "[]";
        private Dictionary<int, List<CategoryAttributeDefinition>> _templatesByCategory = new();

        public async Task<IActionResult> OnGet()
        {
            await LoadCategoriesAsync();
            ImagePreviews = BuildImagesFromFields();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await LoadCategoriesAsync();

            ValidateUploads(Input.ImageFiles);
            if (!ModelState.IsValid)
            {
                ImagePreviews = BuildImagesFromFields();
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

            var variants = BuildVariants(Input);
            if (Input.HasVariants && variants.Count == 0)
            {
                ModelState.AddModelError(nameof(Input.VariantJson), "Provide at least one valid variant.");
                ImagePreviews = BuildImagesFromFields();
                return Page();
            }

            var existingSku = await _productRepository.GetBySku(user.Id, Input.MerchantSku.Trim(), includeDrafts: true);
            if (existingSku != null)
            {
                ModelState.AddModelError(nameof(Input.MerchantSku), "This SKU is already used by another product.");
                ImagePreviews = BuildImagesFromFields();
                return Page();
            }

            var templates = await _categoryAttributes.GetForCategoryAsync(Input.CategoryId.Value);
            var attributes = ValidateAndBuildAttributes(templates, Input.AttributeValues, ModelState);
            if (!ModelState.IsValid)
            {
                ImagePreviews = BuildImagesFromFields();
                return Page();
            }

            var savedImages = await SaveUploadsAsync(user.Id);
            var allImages = savedImages.ToList();
            foreach (var existing in BuildImagesFromFields())
            {
                if (!allImages.Contains(existing))
                {
                    allImages.Add(existing);
                }
            }
            allImages = allImages.Distinct().ToList();
            var mainImage = SelectMainImage(Input.SelectedMainImage, allImages);
            var galleryImages = BuildGallery(allImages, mainImage);

            var product = new ProductModel
            {
                Title = Input.Title.Trim(),
                MerchantSku = Input.MerchantSku.Trim(),
                Price = Input.HasVariants && variants.Any() ? variants.First().Price : Input.Price,
                Stock = Input.HasVariants ? variants.Sum(v => v.Stock) : Input.Stock,
                Category = category.FullPath,
                CategoryId = category.Id,
                Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
                MainImageUrl = mainImage,
                GalleryImageUrls = galleryImages,
                WeightKg = Input.WeightKg,
                LengthCm = Input.LengthCm,
                WidthCm = Input.WidthCm,
                HeightCm = Input.HeightCm,
                ShippingMethods = string.IsNullOrWhiteSpace(Input.ShippingMethods) ? null : Input.ShippingMethods.Trim(),
                HasVariants = Input.HasVariants && variants.Any(),
                VariantData = ProductVariantSerializer.Serialize(variants),
                Attributes = attributes,
                WorkflowState = ProductWorkflowStates.Draft,
                SellerId = user.Id
            };

            Input.MainImageUrl = mainImage;
            Input.GalleryImageUrls = galleryImages;

            await _createProduct.CreateAsync(product);
            await _photoModerationService.SyncFromProductAsync(product, HttpContext.RequestAborted);

            TempData["StatusMessage"] = "Product saved as draft.";
            return RedirectToPage("List");
        }

        public class InputModel
        {
            public bool HasVariants { get; set; }

            [MaxLength(8000)]
            [Display(Name = "Variant definitions (JSON)")]
            public string? VariantJson { get; set; }

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

            [Display(Name = "Upload images")]
            public List<IFormFile> ImageFiles { get; set; } = new();

            [Display(Name = "Main image")]
            public string? SelectedMainImage { get; set; }

            [Display(Name = "Attributes")]
            public Dictionary<int, string> AttributeValues { get; set; } = new();
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

            _templatesByCategory = await _categoryAttributes.GetForCategoriesAsync(tree.Select(c => c.Id), includeDeprecated: false);
            AttributeTemplatesJson = JsonSerializer.Serialize(_templatesByCategory.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Select(v => new
                {
                    v.Id,
                    v.Name,
                    v.Type,
                    v.IsRequired,
                    v.Options
                })));
        }

        private void ValidateUploads(IEnumerable<IFormFile> files)
        {
            foreach (var file in files.Where(f => f != null))
            {
                var validationError = _productImageService.Validate(file);
                if (!string.IsNullOrEmpty(validationError))
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ImageFiles)}", validationError);
                    return;
                }
            }
        }

        private async Task<List<string>> SaveUploadsAsync(string sellerId)
        {
            var saved = new List<string>();
            foreach (var file in Input.ImageFiles.Where(f => f != null))
            {
                var result = await _productImageService.SaveOptimizedAsync(file, sellerId, HttpContext.RequestAborted);
                saved.Add(result.OptimizedUrl);
            }

            return saved;
        }

        private static string? SelectMainImage(string? selectedMain, List<string> images)
        {
            if (!images.Any())
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(selectedMain) && images.Contains(selectedMain))
            {
                return selectedMain;
            }

            return images.First();
        }

        private static string? BuildGallery(List<string> images, string? mainImage)
        {
            if (!images.Any())
            {
                return null;
            }

            var gallery = images
                .Where(i => string.IsNullOrWhiteSpace(mainImage) || !string.Equals(i, mainImage, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return gallery.Any() ? string.Join(", ", gallery) : null;
        }

        private List<string> BuildImagesFromFields()
        {
            var images = new List<string>();
            if (!string.IsNullOrWhiteSpace(Input.MainImageUrl))
            {
                images.Add(Input.MainImageUrl);
            }

            if (!string.IsNullOrWhiteSpace(Input.GalleryImageUrls))
            {
                images.AddRange(Input.GalleryImageUrls.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            ImagePreviews = images.Distinct().ToList();
            return ImagePreviews;
        }

        private static Dictionary<string, string> ValidateAndBuildAttributes(IEnumerable<CategoryAttributeDefinition> templates, Dictionary<int, string> values, ModelStateDictionary modelState)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var provided = values ?? new Dictionary<int, string>();

            foreach (var template in templates)
            {
                provided.TryGetValue(template.Id, out var rawValue);
                var value = rawValue?.Trim();

                if (template.IsRequired && string.IsNullOrWhiteSpace(value))
                {
                    modelState.AddModelError($"{nameof(Input)}.{nameof(Input.AttributeValues)}", $"{template.Name} is required.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (template.Type == CategoryAttributeTypes.Number && !decimal.TryParse(value, out _))
                {
                    modelState.AddModelError($"{nameof(Input)}.{nameof(Input.AttributeValues)}", $"{template.Name} must be a number.");
                    continue;
                }

                if (template.Type == CategoryAttributeTypes.List)
                {
                    var allowed = ParseOptions(template.Options);
                    if (allowed.Any() && !allowed.Contains(value))
                    {
                        modelState.AddModelError($"{nameof(Input)}.{nameof(Input.AttributeValues)}", $"{template.Name} must use an allowed option.");
                        continue;
                    }
                }

                result[template.Name] = value;
            }

            return result;
        }

        private static HashSet<string> ParseOptions(string? options)
        {
            if (string.IsNullOrWhiteSpace(options))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return options
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static List<ProductVariant> BuildVariants(InputModel input)
        {
            if (!input.HasVariants)
            {
                return new List<ProductVariant>();
            }

            var variants = ProductVariantSerializer.Deserialize(input.VariantJson);
            return variants
                .Where(v => v != null && v.Price > 0 && v.Stock >= 0)
                .ToList();
        }
    }
}
