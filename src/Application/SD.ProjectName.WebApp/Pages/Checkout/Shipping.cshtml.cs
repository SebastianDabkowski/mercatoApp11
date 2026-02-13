using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Checkout
{
    public class ShippingModel : PageModel
    {
        private readonly CartViewService _cartViewService;
        private readonly IUserCartService _userCartService;
        private readonly CheckoutStateService _checkoutStateService;
        private readonly ShippingOptionsService _shippingOptionsService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SellerShippingMethodService _sellerShippingMethodService;

        public CartSummary Summary { get; private set; } = CartSummary.Empty;

        public List<SellerShippingOptions> SellerOptions { get; private set; } = new();

        [BindProperty]
        public ShippingSelectionInput Input { get; set; } = new();

        public ShippingModel(
            CartViewService cartViewService,
            IUserCartService userCartService,
            CheckoutStateService checkoutStateService,
            ShippingOptionsService shippingOptionsService,
            UserManager<ApplicationUser> userManager,
            SellerShippingMethodService sellerShippingMethodService)
        {
            _cartViewService = cartViewService;
            _userCartService = userCartService;
            _checkoutStateService = checkoutStateService;
            _shippingOptionsService = shippingOptionsService;
            _userManager = userManager;
            _sellerShippingMethodService = sellerShippingMethodService;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);
            var summary = await _cartViewService.BuildAsync(HttpContext);
            if (summary.IsEmpty)
            {
                return RedirectToPage("/Cart");
            }

            var state = _checkoutStateService.Get(HttpContext);
            if (state?.Address == null)
            {
                return RedirectToPage("/Checkout/Address");
            }

            var sellerCountries = await LoadSellerCountriesAsync(summary.SellerGroups.Select(g => g.SellerId));
            var sellerMethods = await LoadSellerMethodsAsync(summary.SellerGroups.Select(g => g.SellerId), state.Address.Country);
            var quote = _shippingOptionsService.BuildQuote(summary, state.Address, sellerCountries, state.ShippingSelections, sellerMethods);

            Summary = quote.Summary;
            SellerOptions = quote.SellerOptions;
            Input.SelectedMethods = new Dictionary<string, string>(quote.SelectedMethods, StringComparer.OrdinalIgnoreCase);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);
            var summary = await _cartViewService.BuildAsync(HttpContext);
            if (summary.IsEmpty)
            {
                return RedirectToPage("/Cart");
            }

            var state = _checkoutStateService.Get(HttpContext);
            if (state?.Address == null)
            {
                return RedirectToPage("/Checkout/Address");
            }

            var sellerCountries = await LoadSellerCountriesAsync(summary.SellerGroups.Select(g => g.SellerId));
            var sellerMethods = await LoadSellerMethodsAsync(summary.SellerGroups.Select(g => g.SellerId), state.Address.Country);
            var quote = _shippingOptionsService.BuildQuote(summary, state.Address, sellerCountries, Input.SelectedMethods, sellerMethods);

            Summary = quote.Summary;
            SellerOptions = quote.SellerOptions;

            foreach (var seller in SellerOptions)
            {
                if (!quote.SelectedMethods.TryGetValue(seller.SellerId, out var methodId) || !seller.Options.Any(o => string.Equals(o.Id, methodId, StringComparison.OrdinalIgnoreCase)))
                {
                    ModelState.AddModelError(string.Empty, $"Select a shipping method for {seller.SellerName}.");
                }
            }

            if (!ModelState.IsValid)
            {
                Input.SelectedMethods = new Dictionary<string, string>(quote.SelectedMethods, StringComparer.OrdinalIgnoreCase);
                return Page();
            }

            _checkoutStateService.SaveShippingSelections(HttpContext, quote.SelectedMethods);
            TempData["CheckoutShippingSaved"] = true;
            return RedirectToPage("/Checkout/Payment");
        }

        private async Task<Dictionary<string, List<SellerShippingMethod>>> LoadSellerMethodsAsync(IEnumerable<string> sellerIds, string? buyerCountry)
        {
            return await _sellerShippingMethodService.GetAvailableForSellersAsync(sellerIds, buyerCountry, HttpContext.RequestAborted);
        }

        private async Task<Dictionary<string, string>> LoadSellerCountriesAsync(IEnumerable<string> sellerIds)
        {
            var countries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sellerId in sellerIds)
            {
                if (string.IsNullOrWhiteSpace(sellerId) || countries.ContainsKey(sellerId))
                {
                    continue;
                }

                var seller = await _userManager.FindByIdAsync(sellerId);
                if (!string.IsNullOrWhiteSpace(seller?.Country))
                {
                    countries[sellerId] = seller!.Country!;
                }
            }

            return countries;
        }
    }

    public class ShippingSelectionInput
    {
        public Dictionary<string, string> SelectedMethods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
