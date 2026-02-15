using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Buyer.Addresses
{
    [Authorize(Policy = Permissions.BuyerPortal)]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ShippingAddressService _shippingAddressService;

        public List<AddressSummary> Addresses { get; private set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        public IndexModel(UserManager<ApplicationUser> userManager, ShippingAddressService shippingAddressService)
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

            await LoadAddressesAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var result = await _shippingAddressService.DeleteAsync(user.Id, id, HttpContext.RequestAborted);
            if (result == ShippingAddressDeleteResult.BlockedByActiveOrder)
            {
                StatusMessage = "You cannot delete this address while it is used by an active order.";
                await LoadAddressesAsync(user);
                return Page();
            }

            StatusMessage = result == ShippingAddressDeleteResult.Deleted
                ? "Address removed."
                : StatusMessage;

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDefaultAsync(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var updated = await _shippingAddressService.SetDefaultAsync(user.Id, id, HttpContext.RequestAborted);
            StatusMessage = updated ? "Default address updated." : "Could not update the default address.";
            return RedirectToPage();
        }

        private async Task LoadAddressesAsync(ApplicationUser user)
        {
            var saved = await _shippingAddressService.GetAddressesAsync(user.Id, HttpContext.RequestAborted);
            var inUse = await _shippingAddressService.FindAddressesUsedInActiveOrdersAsync(user.Id, saved, HttpContext.RequestAborted);

            Addresses = saved.Select(a => new AddressSummary
            {
                Id = a.Id,
                Recipient = a.Recipient,
                Line1 = a.Line1,
                Line2 = a.Line2,
                City = a.City,
                State = a.State,
                PostalCode = a.PostalCode,
                Country = a.Country,
                Phone = a.Phone,
                IsDefault = a.IsDefault,
                CanDelete = !inUse.Contains(a.Id)
            }).ToList();
        }
    }

    public class AddressSummary
    {
        public int Id { get; set; }

        public string Recipient { get; set; } = string.Empty;

        public string Line1 { get; set; } = string.Empty;

        public string? Line2 { get; set; }

        public string City { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;

        public string PostalCode { get; set; } = string.Empty;

        public string Country { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public bool IsDefault { get; set; }

        public bool CanDelete { get; set; }
    }
}
