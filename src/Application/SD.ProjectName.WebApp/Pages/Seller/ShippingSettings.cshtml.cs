using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class ShippingSettingsModel : PageModel
    {
        private readonly SellerShippingMethodService _shippingMethodService;
        private readonly UserManager<ApplicationUser> _userManager;

        public ShippingSettingsModel(
            SellerShippingMethodService shippingMethodService,
            UserManager<ApplicationUser> userManager)
        {
            _shippingMethodService = shippingMethodService;
            _userManager = userManager;
        }

        public List<SellerShippingMethod> Methods { get; private set; } = new();

        [BindProperty]
        public ShippingMethodInput Input { get; set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(Guid? id = null)
        {
            var owner = await _userManager.GetUserAsync(User);
            if (owner == null)
            {
                return Challenge();
            }

            var storeOwnerId = ResolveStoreOwnerId(owner);
            Methods = await _shippingMethodService.GetForStoreAsync(storeOwnerId, HttpContext.RequestAborted);

            if (id.HasValue)
            {
                var method = Methods.FirstOrDefault(m => m.Id == id.Value);
                if (method != null)
                {
                    Input = new ShippingMethodInput
                    {
                        Id = method.Id,
                        Name = method.Name,
                        Description = method.Description,
                        Availability = method.Availability ?? string.Empty,
                        IsActive = method.IsActive && !method.IsDeleted
                    };
                }
            }

            return Page();
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            var owner = await _userManager.GetUserAsync(User);
            if (owner == null)
            {
                return Challenge();
            }

            var storeOwnerId = ResolveStoreOwnerId(owner);
            if (!ModelState.IsValid)
            {
                Methods = await _shippingMethodService.GetForStoreAsync(storeOwnerId, HttpContext.RequestAborted);
                return Page();
            }

            await _shippingMethodService.SaveAsync(
                storeOwnerId,
                Input.Id,
                Input.Name,
                Input.Description,
                Input.Availability,
                Input.IsActive,
                HttpContext.RequestAborted);

            StatusMessage = Input.Id.HasValue ? "Shipping method updated." : "Shipping method added.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(Guid id)
        {
            var owner = await _userManager.GetUserAsync(User);
            if (owner == null)
            {
                return Challenge();
            }

            await _shippingMethodService.ArchiveAsync(id, ResolveStoreOwnerId(owner), HttpContext.RequestAborted);
            StatusMessage = "Shipping method removed from checkout.";
            return RedirectToPage();
        }

        private static string ResolveStoreOwnerId(ApplicationUser owner)
        {
            return string.IsNullOrWhiteSpace(owner.StoreOwnerId) ? owner.Id : owner.StoreOwnerId;
        }
    }

    public class ShippingMethodInput
    {
        public Guid? Id { get; set; }

        [Required]
        [StringLength(128)]
        [Display(Name = "Method name")]
        public string Name { get; set; } = string.Empty;

        [StringLength(1024)]
        [Display(Name = "Description (optional)")]
        public string? Description { get; set; }

        [StringLength(256)]
        [Display(Name = "Availability (countries/regions)")]
        public string? Availability { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;
    }
}
