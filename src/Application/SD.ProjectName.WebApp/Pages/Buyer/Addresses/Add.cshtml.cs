using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Pages.Checkout;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Buyer.Addresses
{
    [Authorize(Roles = AccountTypes.Buyer)]
    public class AddModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ShippingAddressService _shippingAddressService;

        [BindProperty]
        public AddressForm Input { get; set; } = new();

        [BindProperty]
        public bool SetAsDefault { get; set; }

        public AddModel(UserManager<ApplicationUser> userManager, ShippingAddressService shippingAddressService)
        {
            _userManager = userManager;
            _shippingAddressService = shippingAddressService;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var existing = await _shippingAddressService.GetAddressesAsync(user.Id, HttpContext.RequestAborted);
            SetAsDefault = existing.Count == 0;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (!_shippingAddressService.IsCountrySupported(Input.Country))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Country)}", "This country is not supported for shipping.");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            await _shippingAddressService.UpsertAsync(user.Id, Input, SetAsDefault, HttpContext.RequestAborted);
            TempData["StatusMessage"] = "Address added.";
            return RedirectToPage("./Index");
        }
    }
}
