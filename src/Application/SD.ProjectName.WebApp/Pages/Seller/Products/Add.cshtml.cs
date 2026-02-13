using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller.Products
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class AddModel : PageModel
    {
        private readonly CreateProduct _createProduct;
        private readonly UserManager<ApplicationUser> _userManager;

        public AddModel(CreateProduct createProduct, UserManager<ApplicationUser> userManager)
        {
            _createProduct = createProduct;
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var product = new ProductModel
            {
                Title = Input.Title.Trim(),
                Price = Input.Price,
                Stock = Input.Stock,
                Category = Input.Category.Trim(),
                Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
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
