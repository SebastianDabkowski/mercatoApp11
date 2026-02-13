using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Checkout
{
    public class AddressModel : PageModel
    {
        private readonly CartViewService _cartViewService;
        private readonly IUserCartService _userCartService;
        private readonly CheckoutStateService _checkoutStateService;
        private readonly ShippingAddressService _shippingAddressService;
        private readonly UserManager<ApplicationUser> _userManager;

        public CartSummary Summary { get; private set; } = CartSummary.Empty;

        public List<SavedAddressOption> SavedAddresses { get; private set; } = new();

        [BindProperty]
        public CheckoutAddressInput Input { get; set; } = new();

        public AddressModel(
            CartViewService cartViewService,
            IUserCartService userCartService,
            CheckoutStateService checkoutStateService,
            ShippingAddressService shippingAddressService,
            UserManager<ApplicationUser> userManager)
        {
            _cartViewService = cartViewService;
            _userCartService = userCartService;
            _checkoutStateService = checkoutStateService;
            _shippingAddressService = shippingAddressService;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);
            Summary = await _cartViewService.BuildAsync(HttpContext);
            if (Summary.IsEmpty)
            {
                return RedirectToPage("/Cart");
            }

            SavedAddresses = await LoadSavedAddressesAsync();
            var state = _checkoutStateService.Get(HttpContext);
            if (state?.Address != null)
            {
                Input.SavedAddressKey = state.SavedAddressKey;
                Input.NewAddress = AddressForm.From(state.Address);
            }
            else if (SavedAddresses.Count > 0)
            {
                var firstComplete = SavedAddresses.FirstOrDefault(a => a.IsDefault && IsAddressComplete(a.Address))
                    ?? SavedAddresses.FirstOrDefault(a => IsAddressComplete(a.Address));
                if (firstComplete != null)
                {
                    Input.SavedAddressKey = firstComplete.Key;
                    Input.NewAddress = AddressForm.From(firstComplete.Address);
                }
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);
            Summary = await _cartViewService.BuildAsync(HttpContext);
            SavedAddresses = await LoadSavedAddressesAsync();

            if (Summary.IsEmpty)
            {
                return RedirectToPage("/Cart");
            }

            var address = await ResolveAddressAsync();
            if (address == null)
            {
                return Page();
            }

            if (!_shippingAddressService.IsCountrySupported(address.Country))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.NewAddress)}.{nameof(Input.NewAddress.Country)}", "We don't currently ship to this country.");
                return Page();
            }

            var blockedSellers = await FindBlockedSellersAsync(address.Country);
            if (blockedSellers.Count > 0)
            {
                ModelState.AddModelError(string.Empty, $"We can't ship items from {string.Join(", ", blockedSellers)} to {address.Country}.");
                return Page();
            }

            if (Input.SaveToProfile && User?.Identity?.IsAuthenticated == true)
            {
                var savedKey = await SaveAddressToProfileAsync(address);
                if (!string.IsNullOrWhiteSpace(savedKey))
                {
                    Input.SavedAddressKey = savedKey;
                }
            }

            _checkoutStateService.Save(HttpContext, Input.SavedAddressKey, address);

            TempData["CheckoutAddressSaved"] = true;
            return RedirectToPage("/Checkout/Shipping");
        }

        private async Task<DeliveryAddress?> ResolveAddressAsync()
        {
            var selectedKey = (Input.SavedAddressKey ?? string.Empty).Trim();
            if (selectedKey.Length > 0 && !string.Equals(selectedKey, "new", StringComparison.OrdinalIgnoreCase))
            {
                var saved = SavedAddresses.FirstOrDefault(a => string.Equals(a.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
                if (saved == null)
                {
                    ModelState.AddModelError(nameof(Input.SavedAddressKey), "The selected address is no longer available.");
                    return null;
                }

                if (!IsAddressComplete(saved.Address))
                {
                    ModelState.AddModelError(nameof(Input.SavedAddressKey), "Saved address is missing required details. Please edit and save a new address.");
                    return null;
                }

                return saved.Address;
            }

            ModelState.ClearValidationState($"{nameof(Input)}.{nameof(Input.NewAddress)}");
            if (!TryValidateModel(Input.NewAddress, $"{nameof(Input)}.{nameof(Input.NewAddress)}"))
            {
                return null;
            }

            return Input.NewAddress.ToDeliveryAddress();
        }

        private async Task<List<SavedAddressOption>> LoadSavedAddressesAsync()
        {
            var addresses = new List<SavedAddressOption>();
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return addresses;
            }

            var saved = await _shippingAddressService.GetAddressesAsync(user.Id, HttpContext.RequestAborted);
            foreach (var savedAddress in saved)
            {
                var delivery = _shippingAddressService.ToDeliveryAddress(savedAddress);
                addresses.Add(new SavedAddressOption(
                    savedAddress.Id.ToString(CultureInfo.InvariantCulture),
                    savedAddress.Recipient,
                    delivery,
                    false,
                    savedAddress.IsDefault));
            }

            if (addresses.Count == 0 && !string.IsNullOrWhiteSpace(user.Address) && !string.IsNullOrWhiteSpace(user.Country))
            {
                var address = new DeliveryAddress(
                    string.IsNullOrWhiteSpace(user.FullName) ? "Saved recipient" : user.FullName,
                    user.Address,
                    null,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    user.Country,
                    null);

                addresses.Add(new SavedAddressOption("profile", "Saved address", address, true, true));
            }

            return addresses
                .OrderByDescending(a => a.IsDefault)
                .ThenBy(a => a.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<string>> FindBlockedSellersAsync(string country)
        {
            if (string.IsNullOrWhiteSpace(country) || Summary.IsEmpty)
            {
                return new List<string>();
            }

            var sellerCountries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sellerIds = Summary.SellerGroups.Select(g => g.SellerId).Distinct().ToList();
            foreach (var sellerId in sellerIds)
            {
                if (string.IsNullOrWhiteSpace(sellerId) || sellerCountries.ContainsKey(sellerId))
                {
                    continue;
                }

                var seller = await _userManager.FindByIdAsync(sellerId);
                if (seller?.Country != null)
                {
                    sellerCountries[sellerId] = seller.Country;
                }
            }

            return ShippingRegionHelper.FindBlockedSellers(Summary.SellerGroups, sellerCountries, country);
        }

        private async Task<string?> SaveAddressToProfileAsync(DeliveryAddress address)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return null;
            }

            user.Address = BuildProfileAddress(address);
            user.Country = address.Country;
            if (!string.IsNullOrWhiteSpace(address.Recipient))
            {
                user.FullName = address.Recipient;
            }

            await _userManager.UpdateAsync(user);
            var saved = await _shippingAddressService.UpsertAsync(user.Id, AddressForm.From(address), true, HttpContext.RequestAborted);
            return saved.Id.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsAddressComplete(DeliveryAddress address)
        {
            return !string.IsNullOrWhiteSpace(address.Recipient)
                && !string.IsNullOrWhiteSpace(address.Line1)
                && !string.IsNullOrWhiteSpace(address.City)
                && !string.IsNullOrWhiteSpace(address.PostalCode)
                && !string.IsNullOrWhiteSpace(address.Country)
                && !string.IsNullOrWhiteSpace(address.Phone);
        }

        private static string BuildProfileAddress(DeliveryAddress address)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(address.Line1))
            {
                parts.Add(address.Line1);
            }
            if (!string.IsNullOrWhiteSpace(address.Line2))
            {
                parts.Add(address.Line2);
            }
            if (!string.IsNullOrWhiteSpace(address.City) || !string.IsNullOrWhiteSpace(address.State))
            {
                var cityState = string.Join(", ", new[] { address.City, address.State }.Where(p => !string.IsNullOrWhiteSpace(p)));
                if (!string.IsNullOrWhiteSpace(cityState))
                {
                    parts.Add(cityState);
                }
            }
            if (!string.IsNullOrWhiteSpace(address.PostalCode))
            {
                parts.Add(address.PostalCode);
            }
            if (!string.IsNullOrWhiteSpace(address.Country))
            {
                parts.Add(address.Country);
            }

            return string.Join(", ", parts);
        }
    }

    public class CheckoutAddressInput
    {
        public string? SavedAddressKey { get; set; }

        public bool SaveToProfile { get; set; }

        public AddressForm NewAddress { get; set; } = new();
    }

    public class AddressForm
    {
        [Required]
        [StringLength(256)]
        public string Recipient { get; set; } = string.Empty;

        [Required]
        [StringLength(256)]
        public string Line1 { get; set; } = string.Empty;

        [StringLength(256)]
        public string? Line2 { get; set; }

        [Required]
        [StringLength(128)]
        public string City { get; set; } = string.Empty;

        [StringLength(128)]
        public string State { get; set; } = string.Empty;

        [Required]
        [StringLength(32)]
        public string PostalCode { get; set; } = string.Empty;

        [Required]
        [StringLength(120)]
        public string Country { get; set; } = string.Empty;

        [Required]
        [StringLength(32)]
        public string? Phone { get; set; }

        public DeliveryAddress ToDeliveryAddress()
        {
            return new DeliveryAddress(Recipient, Line1, Line2, City, State, PostalCode, Country, Phone);
        }

        public static AddressForm From(DeliveryAddress address)
        {
            return new AddressForm
            {
                Recipient = address.Recipient,
                Line1 = address.Line1,
                Line2 = address.Line2,
                City = address.City,
                State = address.State,
                PostalCode = address.PostalCode,
                Country = address.Country,
                Phone = address.Phone
            };
        }
    }

    public record SavedAddressOption(string Key, string Label, DeliveryAddress Address, bool IsProfileAddress, bool IsDefault);

    public static class ShippingRegionHelper
    {
        public static List<string> FindBlockedSellers(IEnumerable<CartSellerGroup> sellerGroups, IReadOnlyDictionary<string, string> sellerCountries, string destinationCountry)
        {
            var blocked = new List<string>();
            if (sellerGroups == null || sellerCountries == null || string.IsNullOrWhiteSpace(destinationCountry))
            {
                return blocked;
            }

            foreach (var group in sellerGroups)
            {
                if (string.IsNullOrWhiteSpace(group.SellerId))
                {
                    continue;
                }

                if (!sellerCountries.TryGetValue(group.SellerId, out var sellerCountry) || string.IsNullOrWhiteSpace(sellerCountry))
                {
                    continue;
                }

                if (!string.Equals(sellerCountry, destinationCountry, StringComparison.OrdinalIgnoreCase))
                {
                    blocked.Add(group.SellerName);
                }
            }

            return blocked;
        }
    }
}
