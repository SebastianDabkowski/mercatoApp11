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
        private readonly ShippingProviderService _shippingProviderService;

        public ShippingSettingsModel(
            SellerShippingMethodService shippingMethodService,
            UserManager<ApplicationUser> userManager,
            ShippingProviderService shippingProviderService)
        {
            _shippingMethodService = shippingMethodService;
            _userManager = userManager;
            _shippingProviderService = shippingProviderService;
        }

        public List<SellerShippingMethod> Methods { get; private set; } = new();
        public List<ProviderServiceOption> ProviderServices { get; private set; } = new();

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
            ProviderServices = _shippingProviderService.GetProviderServices();

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
                        BaseCost = method.BaseCost,
                        DeliveryEstimate = method.DeliveryEstimate,
                        Availability = method.Availability ?? string.Empty,
                        IsActive = method.IsActive && !method.IsDeleted,
                        ProviderServiceKey = string.IsNullOrWhiteSpace(method.ProviderId) || string.IsNullOrWhiteSpace(method.ProviderServiceCode)
                            ? null
                            : $"{method.ProviderId}:{method.ProviderServiceCode}"
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
            ProviderServices = _shippingProviderService.GetProviderServices();
            if (!ModelState.IsValid)
            {
                Methods = await _shippingMethodService.GetForStoreAsync(storeOwnerId, HttpContext.RequestAborted);
                return Page();
            }

            string? providerId = null;
            string? providerServiceCode = null;
            if (!string.IsNullOrWhiteSpace(Input.ProviderServiceKey))
            {
                var tokens = Input.ProviderServiceKey.Split(':', 2, StringSplitOptions.TrimEntries);
                if (tokens.Length == 2)
                {
                    providerId = string.IsNullOrWhiteSpace(tokens[0]) ? null : tokens[0];
                    providerServiceCode = string.IsNullOrWhiteSpace(tokens[1]) ? null : tokens[1];
                }
            }

            var saved = await _shippingMethodService.SaveAsync(
                storeOwnerId,
                Input.Id,
                Input.Name,
                Input.Description,
                Input.BaseCost,
                Input.DeliveryEstimate,
                Input.Availability,
                Input.IsActive,
                providerId,
                providerServiceCode,
                HttpContext.RequestAborted);

            if (saved == null)
            {
                Methods = await _shippingMethodService.GetForStoreAsync(storeOwnerId, HttpContext.RequestAborted);
                ModelState.AddModelError(string.Empty, "Selected provider service is not available.");
                return Page();
            }

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

        [Range(0, double.MaxValue)]
        [Display(Name = "Base cost")]
        public decimal BaseCost { get; set; }

        [StringLength(128)]
        [Display(Name = "Estimated delivery time")]
        public string? DeliveryEstimate { get; set; }

        [StringLength(256)]
        [Display(Name = "Availability (countries/regions)")]
        public string? Availability { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Integrated provider service (optional)")]
        public string? ProviderServiceKey { get; set; }
    }
}
