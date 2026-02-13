using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Checkout
{
    public class ConfirmationModel : PageModel
    {
        private readonly CartViewService _cartViewService;
        private readonly IUserCartService _userCartService;
        private readonly CheckoutStateService _checkoutStateService;
        private readonly ShippingOptionsService _shippingOptionsService;
        private readonly CheckoutOptions _checkoutOptions;
        private readonly UserManager<ApplicationUser> _userManager;

        public CartSummary Summary { get; private set; } = CartSummary.Empty;

        public DeliveryAddress? Address { get; private set; }

        public string? PaymentMethodLabel { get; private set; }

        public string? PaymentReference { get; private set; }

        public ConfirmationModel(
            CartViewService cartViewService,
            IUserCartService userCartService,
            CheckoutStateService checkoutStateService,
            ShippingOptionsService shippingOptionsService,
            CheckoutOptions checkoutOptions,
            UserManager<ApplicationUser> userManager)
        {
            _cartViewService = cartViewService;
            _userCartService = userCartService;
            _checkoutStateService = checkoutStateService;
            _shippingOptionsService = shippingOptionsService;
            _checkoutOptions = checkoutOptions;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);
            var summary = await _cartViewService.BuildAsync(HttpContext);
            var state = _checkoutStateService.Get(HttpContext);
            if (state?.Address == null)
            {
                return RedirectToPage("/Checkout/Address");
            }

            if (state.PaymentStatus != CheckoutPaymentStatus.Confirmed)
            {
                return RedirectToPage("/Checkout/Payment");
            }

            Address = state.Address;
            PaymentReference = state.PaymentReference;
            PaymentMethodLabel = ResolvePaymentLabel(state.PaymentMethod);

            var sellerCountries = await LoadSellerCountriesAsync(summary.SellerGroups.Select(g => g.SellerId));
            var quote = _shippingOptionsService.BuildQuote(summary, state.Address, sellerCountries, state.ShippingSelections);
            Summary = quote.Summary;

            _checkoutStateService.Clear(HttpContext);
            return Page();
        }

        private string? ResolvePaymentLabel(string? methodId)
        {
            if (string.IsNullOrWhiteSpace(methodId))
            {
                return null;
            }

            var match = _checkoutOptions.PaymentMethods?.FirstOrDefault(m => string.Equals(m.Id, methodId, StringComparison.OrdinalIgnoreCase));
            return match?.Label ?? methodId;
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
}
