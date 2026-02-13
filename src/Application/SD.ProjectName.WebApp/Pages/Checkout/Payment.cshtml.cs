using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Checkout
{
    public class PaymentModel : PageModel
    {
        private readonly CartViewService _cartViewService;
        private readonly IUserCartService _userCartService;
        private readonly CheckoutStateService _checkoutStateService;
        private readonly ShippingOptionsService _shippingOptionsService;
        private readonly CheckoutOptions _checkoutOptions;
        private readonly UserManager<ApplicationUser> _userManager;

        public CartSummary Summary { get; private set; } = CartSummary.Empty;

        public List<PaymentMethodOption> Methods { get; private set; } = new();

        public CheckoutPaymentStatus PaymentStatus { get; private set; } = CheckoutPaymentStatus.None;

        public string? PaymentReference { get; private set; }

        [BindProperty]
        public PaymentInput Input { get; set; } = new();

        public PaymentModel(
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

        public async Task<IActionResult> OnGetAsync(string? providerResult = null, string? method = null)
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

            if (state.ShippingSelections == null || state.ShippingSelections.Count == 0)
            {
                return RedirectToPage("/Checkout/Shipping");
            }

            var sellerCountries = await LoadSellerCountriesAsync(summary.SellerGroups.Select(g => g.SellerId));
            var quote = _shippingOptionsService.BuildQuote(summary, state.Address, sellerCountries, state.ShippingSelections);
            Summary = quote.Summary;

            Methods = _checkoutOptions.PaymentMethods ?? new List<PaymentMethodOption>();
            PaymentStatus = state.PaymentStatus;
            PaymentReference = state.PaymentReference;

            if (!string.IsNullOrWhiteSpace(providerResult))
            {
                var result = await HandleProviderReturn(providerResult, method ?? state.PaymentMethod, state);
                if (result != null)
                {
                    return result;
                }
            }

            var selected = state.PaymentMethod;
            if (string.IsNullOrWhiteSpace(selected))
            {
                selected = Methods.FirstOrDefault()?.Id;
            }

            Input.SelectedMethodId = selected ?? string.Empty;
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

            if (state.ShippingSelections == null || state.ShippingSelections.Count == 0)
            {
                return RedirectToPage("/Checkout/Shipping");
            }

            var sellerCountries = await LoadSellerCountriesAsync(summary.SellerGroups.Select(g => g.SellerId));
            var quote = _shippingOptionsService.BuildQuote(summary, state.Address, sellerCountries, state.ShippingSelections);
            Summary = quote.Summary;

            Methods = _checkoutOptions.PaymentMethods ?? new List<PaymentMethodOption>();

            var selected = Methods.FirstOrDefault(m => string.Equals(m.Id, Input.SelectedMethodId, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                ModelState.AddModelError(nameof(Input.SelectedMethodId), "Select a payment method.");
                return Page();
            }

            if (selected.RequiresRedirect)
            {
                _checkoutStateService.SavePaymentSelection(HttpContext, selected.Id, CheckoutPaymentStatus.Pending, null);
                var callbackUrl = Url.Page("/Checkout/Payment", null, new { providerResult = "success", method = selected.Id }, Request.Scheme);
                var cancelUrl = Url.Page("/Checkout/Payment", null, new { providerResult = "cancel", method = selected.Id }, Request.Scheme);
                TempData["PaymentRedirect"] = $"Redirecting to {selected.Provider ?? "payment provider"}...";
                TempData["PaymentCancelUrl"] = cancelUrl;
                return Redirect(callbackUrl ?? "/Checkout/Payment");
            }

            _checkoutStateService.SavePaymentSelection(HttpContext, selected.Id, CheckoutPaymentStatus.Confirmed, GenerateReference(selected.Id));
            return RedirectToPage("/Checkout/Confirmation");
        }

        private async Task<IActionResult?> HandleProviderReturn(string providerResult, string? methodId, CheckoutState state)
        {
            var normalized = providerResult.Trim().ToLowerInvariant();
            var selectedMethod = Methods.FirstOrDefault(m => string.Equals(m.Id, methodId, StringComparison.OrdinalIgnoreCase))
                ?? Methods.FirstOrDefault(m => string.Equals(m.Id, state.PaymentMethod, StringComparison.OrdinalIgnoreCase));

            if (selectedMethod == null)
            {
                PaymentStatus = CheckoutPaymentStatus.Failed;
                ModelState.AddModelError(string.Empty, "Selected payment method is no longer available.");
                return Page();
            }

            if (normalized == "success")
            {
                _checkoutStateService.SavePaymentSelection(HttpContext, selectedMethod.Id, CheckoutPaymentStatus.Confirmed, GenerateReference(selectedMethod.Id));
                return RedirectToPage("/Checkout/Confirmation");
            }

            if (normalized is "cancel" or "canceled")
            {
                PaymentStatus = CheckoutPaymentStatus.Canceled;
                _checkoutStateService.SavePaymentSelection(HttpContext, selectedMethod.Id, CheckoutPaymentStatus.Canceled, null);
                Input.SelectedMethodId = selectedMethod.Id;
                TempData["PaymentMessage"] = "Payment was cancelled. You can choose another method.";
                return Page();
            }

            PaymentStatus = CheckoutPaymentStatus.Failed;
            _checkoutStateService.SavePaymentSelection(HttpContext, selectedMethod.Id, CheckoutPaymentStatus.Failed, null);
            Input.SelectedMethodId = selectedMethod.Id;
            ModelState.AddModelError(string.Empty, "Payment authorization failed. Please try again or choose another method.");
            return Page();
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

        private static string GenerateReference(string methodId)
        {
            return $"{methodId}-{Guid.NewGuid():N}".ToLowerInvariant();
        }
    }

    public class PaymentInput
    {
        public string SelectedMethodId { get; set; } = string.Empty;
    }
}
