using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;
using System.Text.Json;
using System.Collections.Generic;

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
        private readonly IProductRepository _productRepository;
        private readonly ProductImageService _productImageService;
        private readonly ProductPhotoModerationService _photoModerationService;
        private readonly ManageCategoryAttributes _categoryAttributes;

        public EditModel(
            GetProducts getProducts,
            UpdateProduct updateProduct,
            UserManager<ApplicationUser> userManager,
            ManageCategories categories,
            ILogger<EditModel> logger,
            IProductRepository productRepository,
            ProductImageService productImageService,
            ProductPhotoModerationService photoModerationService,
            ManageCategoryAttributes categoryAttributes)
        {
            _getProducts = getProducts;
            _updateProduct = updateProduct;
            _userManager = userManager;
            _categories = categories;
            _logger = logger;
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
        public Dictionary<string, string> LegacyAttributes { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, List<CategoryAttributeDefinition>> _templatesByCategory = new();

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
                MerchantSku = product.MerchantSku,
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
                ShippingMethods = product.ShippingMethods,
                HasVariants = product.HasVariants,
                VariantJson = product.VariantData
            };

            await LoadCategoriesAsync(product.CategoryId);
            await LoadAttributeTemplatesAsync(CategoryOptions.Select(o => int.Parse(o.Value)), product.CategoryId, includeDeprecatedForSelected: true);
            MapExistingAttributes(product);
            ImagePreviews = BuildImages(product.MainImageUrl, product.GalleryImageUrls);
            Input.SelectedMainImage = product.MainImageUrl;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await LoadCategoriesAsync(Input.CategoryId);
            var categoryIds = CategoryOptions.Select(o => int.Parse(o.Value)).ToList();
            await LoadAttributeTemplatesAsync(categoryIds, Input.CategoryId, includeDeprecatedForSelected: true);

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
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

            MapExistingAttributes(product);

            var currentImages = BuildImages(product.MainImageUrl, product.GalleryImageUrls);
            currentImages.AddRange(BuildImages(Input.MainImageUrl, Input.GalleryImageUrls));
            currentImages = currentImages.Distinct().ToList();
            ValidateUploads(Input.ImageFiles);
            if (!ModelState.IsValid)
            {
                ImagePreviews = currentImages;
                Input.SelectedMainImage ??= Input.MainImageUrl ?? product.MainImageUrl;
                return Page();
            }

            if (!Input.CategoryId.HasValue)
            {
                ModelState.AddModelError(nameof(Input.CategoryId), "Select a category.");
                ImagePreviews = currentImages;
                Input.SelectedMainImage ??= Input.MainImageUrl ?? product.MainImageUrl;
                return Page();
            }

            var category = await _categories.GetById(Input.CategoryId.Value, includeInactive: true);
            if (category == null || (!category.IsActive && product.CategoryId != category.Id))
            {
                ModelState.AddModelError(nameof(Input.CategoryId), "Select a valid active category.");
                ImagePreviews = currentImages;
                Input.SelectedMainImage ??= Input.MainImageUrl ?? product.MainImageUrl;
                return Page();
            }

            var otherWithSku = await _productRepository.GetBySku(user.Id, Input.MerchantSku.Trim(), includeDrafts: true);
            if (otherWithSku != null && otherWithSku.Id != product.Id)
            {
                ModelState.AddModelError(nameof(Input.MerchantSku), "This SKU is already used by another product.");
                ImagePreviews = currentImages;
                Input.SelectedMainImage ??= Input.MainImageUrl ?? product.MainImageUrl;
                return Page();
            }

            var variants = BuildVariants(Input);
            if (Input.HasVariants && variants.Count == 0)
            {
                ModelState.AddModelError(nameof(Input.VariantJson), "Provide at least one valid variant.");
                ImagePreviews = currentImages;
                Input.SelectedMainImage ??= Input.MainImageUrl ?? product.MainImageUrl;
                return Page();
            }

            var templates = await _categoryAttributes.GetForCategoryAsync(Input.CategoryId.Value, includeDeprecated: true);
            var attributes = ValidateAndBuildAttributes(templates, Input.AttributeValues, ModelState, product.Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            if (!ModelState.IsValid)
            {
                ImagePreviews = currentImages;
                Input.SelectedMainImage ??= Input.MainImageUrl ?? product.MainImageUrl;
                return Page();
            }

            var savedImages = await SaveUploadsAsync(user.Id);
            var allImages = currentImages.Concat(savedImages).Distinct().ToList();
            var mainImage = SelectMainImage(Input.SelectedMainImage ?? product.MainImageUrl, allImages);
            var galleryImages = BuildGallery(allImages, mainImage);

            product.Title = Input.Title.Trim();
            product.MerchantSku = Input.MerchantSku.Trim();
            product.Price = Input.HasVariants && variants.Any() ? variants.First().Price : Input.Price;
            product.Stock = Input.HasVariants ? variants.Sum(v => v.Stock) : Input.Stock;
            product.CategoryId = category.Id;
            product.Category = category.FullPath;
            product.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();
            product.MainImageUrl = mainImage;
            product.GalleryImageUrls = galleryImages;
            product.WeightKg = Input.WeightKg;
            product.LengthCm = Input.LengthCm;
            product.WidthCm = Input.WidthCm;
            product.HeightCm = Input.HeightCm;
            product.ShippingMethods = string.IsNullOrWhiteSpace(Input.ShippingMethods) ? null : Input.ShippingMethods.Trim();
            product.HasVariants = Input.HasVariants && variants.Any();
            product.VariantData = ProductVariantSerializer.Serialize(variants);
            product.Attributes = attributes;

            await _updateProduct.UpdateAsync(product);
            _logger.LogInformation("Product {ProductId} updated by seller {SellerId}", product.Id, user.Id);
            await _photoModerationService.SyncFromProductAsync(product, HttpContext.RequestAborted);

            Input.MainImageUrl = mainImage;
            Input.GalleryImageUrls = galleryImages;
            TempData["StatusMessage"] = "Product updated.";
            return RedirectToPage("List");
        }

        public class InputModel
        {
            public bool HasVariants { get; set; }

            [MaxLength(8000)]
            [Display(Name = "Variant definitions (JSON)")]
            public string? VariantJson { get; set; }

            [Required]
            public int Id { get; set; }

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

        private async Task LoadAttributeTemplatesAsync(IEnumerable<int> categoryIds, int? selectedCategoryId, bool includeDeprecatedForSelected)
        {
            _templatesByCategory = await _categoryAttributes.GetForCategoriesAsync(categoryIds, includeDeprecated: false);
            if (selectedCategoryId.HasValue && includeDeprecatedForSelected)
            {
                var selectedTemplates = await _categoryAttributes.GetForCategoryAsync(selectedCategoryId.Value, includeDeprecated: true);
                _templatesByCategory[selectedCategoryId.Value] = selectedTemplates;
            }

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

        private void MapExistingAttributes(ProductModel product)
        {
            var existing = product.Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var selectedCategoryId = product.CategoryId ?? 0;
            var templates = _templatesByCategory.TryGetValue(selectedCategoryId, out var list)
                ? list
                : new List<CategoryAttributeDefinition>();

            foreach (var pair in existing)
            {
                var match = templates.FirstOrDefault(t => string.Equals(t.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    if (!Input.AttributeValues.ContainsKey(match.Id))
                    {
                        Input.AttributeValues[match.Id] = pair.Value;
                    }
                }
                else
                {
                    LegacyAttributes[pair.Key] = pair.Value;
                }
            }
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

        private static List<string> BuildImages(string? mainImage, string? galleryImages)
        {
            var images = new List<string>();
            if (!string.IsNullOrWhiteSpace(mainImage))
            {
                images.Add(mainImage);
            }

            if (!string.IsNullOrWhiteSpace(galleryImages))
            {
                images.AddRange(galleryImages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            return images.Distinct().ToList();
        }

        private static Dictionary<string, string> ValidateAndBuildAttributes(IEnumerable<CategoryAttributeDefinition> templates, Dictionary<int, string> values, ModelStateDictionary modelState, Dictionary<string, string> startingValues)
        {
            var result = new Dictionary<string, string>(startingValues, StringComparer.OrdinalIgnoreCase);
            var provided = values ?? new Dictionary<int, string>();

            foreach (var template in templates)
            {
                provided.TryGetValue(template.Id, out var rawValue);
                var value = rawValue?.Trim();

                if (template.IsRequired && !template.IsDeprecated && string.IsNullOrWhiteSpace(value))
                {
                    modelState.AddModelError($"{nameof(Input)}.{nameof(Input.AttributeValues)}", $"{template.Name} is required.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    if (result.ContainsKey(template.Name))
                    {
                        result.Remove(template.Name);
                    }
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
