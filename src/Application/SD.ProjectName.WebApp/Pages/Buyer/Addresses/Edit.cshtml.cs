using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Pages.Checkout;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Buyer.Addresses
{
    [Authorize(Policy = Permissions.BuyerPortal)]
    public class EditModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ShippingAddressService _shippingAddressService;

        [BindProperty]
        public AddressForm Input { get; set; } = new();

        [BindProperty]
        public bool SetAsDefault { get; set; }

        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }

        public EditModel(UserManager<ApplicationUser> userManager, ShippingAddressService shippingAddressService)
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

            var address = await _shippingAddressService.GetAsync(user.Id, Id, HttpContext.RequestAborted);
            if (address == null)
            {
                return RedirectToPage("./Index");
            }

            Input = AddressForm.From(_shippingAddressService.ToDeliveryAddress(address));
            SetAsDefault = address.IsDefault;
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

            var updated = await _shippingAddressService.UpdateAsync(user.Id, Id, Input, SetAsDefault, HttpContext.RequestAborted);
            if (updated == null)
            {
                return RedirectToPage("./Index");
            }

            TempData["StatusMessage"] = "Address updated.";
            return RedirectToPage("./Index");
        }
    }
}
