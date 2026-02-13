using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
        private readonly ILogger<EditModel> _logger;

        public EditModel(
            GetProducts getProducts,
            UpdateProduct updateProduct,
            UserManager<ApplicationUser> userManager,
            ILogger<EditModel> logger)
        {
            _getProducts = getProducts;
            _updateProduct = updateProduct;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

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
                Category = product.Category,
                Description = product.Description
            };

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
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

            product.Title = Input.Title.Trim();
            product.Price = Input.Price;
            product.Stock = Input.Stock;
            product.Category = Input.Category.Trim();
            product.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();

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

            [Required]
            [MaxLength(100)]
            public string Category { get; set; } = string.Empty;

            [MaxLength(1000)]
            public string? Description { get; set; }
        }
    }
}
