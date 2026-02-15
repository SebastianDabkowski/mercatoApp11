using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Data;
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
        private readonly OrderService _orderService;
        private readonly CartService _cartService;
        private readonly PromoCodeService _promoCodeService;
        private readonly PaymentProviderService _paymentProvider;
        private readonly SellerShippingMethodService _sellerShippingMethodService;
        private readonly CurrencyConfigurationService _currencyConfiguration;
        private readonly IntegrationManagementService _integrationService;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

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
            UserManager<ApplicationUser> userManager,
            OrderService orderService,
            CartService cartService,
            PromoCodeService promoCodeService,
            PaymentProviderService paymentProvider,
            SellerShippingMethodService sellerShippingMethodService,
            CurrencyConfigurationService currencyConfiguration,
            IntegrationManagementService integrationService)
        {
            _cartViewService = cartViewService;
            _userCartService = userCartService;
            _checkoutStateService = checkoutStateService;
            _shippingOptionsService = shippingOptionsService;
            _checkoutOptions = checkoutOptions;
            _userManager = userManager;
            _orderService = orderService;
            _cartService = cartService;
            _promoCodeService = promoCodeService;
            _paymentProvider = paymentProvider;
            _sellerShippingMethodService = sellerShippingMethodService;
            _currencyConfiguration = currencyConfiguration;
            _integrationService = integrationService;
        }

        public async Task<IActionResult> OnGetAsync(string? providerToken = null, string? method = null)
        {
            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);
            var summary = await _cartViewService.BuildAsync(HttpContext);
            if (summary.IsEmpty)
            {
                TempData["PaymentMessage"] = "Some items are no longer available or have insufficient stock. Please update your cart and try again.";
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
            var sellerMethods = await LoadSellerMethodsAsync(summary.SellerGroups.Select(g => g.SellerId), state.Address.Country);
            var quote = _shippingOptionsService.BuildQuote(summary, state.Address, sellerCountries, state.ShippingSelections, sellerMethods);
            Summary = quote.Summary;
            Input.CartSignature = ComputeQuoteSignature(quote);

            Methods = GetEnabledMethods();
            PaymentStatus = state.PaymentStatus;
            PaymentReference = state.PaymentReference;

            var integrationAvailable = await EnsurePaymentIntegrationEnabledAsync();
            if (!integrationAvailable)
            {
                return Page();
            }

            if (Methods.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "No payment methods are available.");
                return Page();
            }

            if (!string.IsNullOrWhiteSpace(providerToken))
            {
                var result = await HandleProviderReturn(providerToken, method ?? state.PaymentMethod, state, quote);
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
                TempData["PaymentMessage"] = "Some items are no longer available or have insufficient stock. Please update your cart and try again.";
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
            var sellerMethods = await LoadSellerMethodsAsync(summary.SellerGroups.Select(g => g.SellerId), state.Address.Country);
            var quote = _shippingOptionsService.BuildQuote(summary, state.Address, sellerCountries, state.ShippingSelections, sellerMethods);
            Summary = quote.Summary;
            var previousSignature = Input.CartSignature;
            var signature = ComputeQuoteSignature(quote);
            Input.CartSignature = signature;

            Methods = GetEnabledMethods();
            PaymentStatus = state.PaymentStatus;
            PaymentReference = state.PaymentReference;

            var integrationAvailable = await EnsurePaymentIntegrationEnabledAsync();
            if (!integrationAvailable)
            {
                return Page();
            }

            if (Methods.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "No payment methods are available.");
                return Page();
            }

            var cartChanged = string.IsNullOrWhiteSpace(previousSignature) || !string.Equals(previousSignature, signature, StringComparison.Ordinal);
            if (cartChanged)
            {
                ModelState.AddModelError(string.Empty, "Cart updated because of stock or price changes. Review the totals and confirm again.");
                return Page();
            }

            var selected = Methods.FirstOrDefault(m => string.Equals(m.Id, Input.SelectedMethodId, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                ModelState.AddModelError(nameof(Input.SelectedMethodId), "Select a payment method.");
                return Page();
            }

            var currency = await ResolveCurrencyAsync();
            if (selected.RequiresRedirect)
            {
                var callbackUrl = Url.Page("/Checkout/Payment", null, new { method = selected.Id }, Request.Scheme) ?? "/Checkout/Payment";
                var cancelUrl = Url.Page("/Checkout/Payment", null, new { method = selected.Id }, Request.Scheme) ?? "/Checkout/Payment";
                var redirect = _paymentProvider.CreateRedirectPayment(new PaymentRedirectRequest(selected.Id, Summary.GrandTotal, currency, callbackUrl, cancelUrl));
                _checkoutStateService.SavePaymentSelection(HttpContext, selected.Id, CheckoutPaymentStatus.Pending, redirect.PaymentReference, signature);
                PaymentStatus = CheckoutPaymentStatus.Pending;
                PaymentReference = redirect.PaymentReference;
                TempData["PaymentRedirect"] = $"Redirecting to {selected.Provider ?? "payment provider"}...";
                TempData["PaymentCancelUrl"] = redirect.CancelUrl;
                return Redirect(string.IsNullOrWhiteSpace(redirect.RedirectUrl) ? callbackUrl : redirect.RedirectUrl);
            }

            if (string.Equals(selected.Id, "blik", StringComparison.OrdinalIgnoreCase))
            {
                var authorization = _paymentProvider.AuthorizeBlik(selected.Id, Summary.GrandTotal, currency, Input.BlikCode);
                var updatedState = _checkoutStateService.SavePaymentSelection(HttpContext, selected.Id, authorization.Status, authorization.PaymentReference, signature);
                PaymentStatus = authorization.Status;
                PaymentReference = authorization.PaymentReference;
                Input.SelectedMethodId = selected.Id;
                Input.CartSignature = signature;

                if (authorization.Status == CheckoutPaymentStatus.Confirmed)
                {
                    StoreSnapshot(quote);
                    return await FinalizeOrderAsync(quote, updatedState);
                }

                await RecordFailedPaymentAsync(quote, updatedState, ResolvePaymentLabel(selected.Id));
                var error = PaymentStatusMapper.BuildBuyerMessage(PaymentStatuses.Failed);
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = "Payment authorization failed. Please try again or choose another method.";
                }

                ModelState.AddModelError(string.Empty, error);
                return Page();
            }

            var confirmedState = _checkoutStateService.SavePaymentSelection(HttpContext, selected.Id, CheckoutPaymentStatus.Confirmed, GenerateReference(selected.Id), signature);
            PaymentStatus = CheckoutPaymentStatus.Confirmed;
            PaymentReference = confirmedState.PaymentReference;
            StoreSnapshot(quote);
            return await FinalizeOrderAsync(quote, confirmedState);
        }

        private async Task<IActionResult?> HandleProviderReturn(string providerToken, string? methodId, CheckoutState state, ShippingQuote quote)
        {
            var selectedMethod = Methods.FirstOrDefault(m => string.Equals(m.Id, methodId, StringComparison.OrdinalIgnoreCase))
                ?? Methods.FirstOrDefault(m => string.Equals(m.Id, state.PaymentMethod, StringComparison.OrdinalIgnoreCase));

            if (selectedMethod == null)
            {
                PaymentStatus = CheckoutPaymentStatus.Failed;
                ModelState.AddModelError(string.Empty, "Selected payment method is no longer available.");
                return Page();
            }

            var currentSignature = ComputeQuoteSignature(quote);
            var currency = await ResolveCurrencyAsync();
            var validation = _paymentProvider.ValidateReturn(providerToken, quote.Summary.GrandTotal, currency, selectedMethod.Id);

            if (string.IsNullOrWhiteSpace(state.CartSignature) || !string.Equals(state.CartSignature, currentSignature, StringComparison.Ordinal))
            {
                PaymentStatus = CheckoutPaymentStatus.Failed;
                _checkoutStateService.SavePaymentSelection(HttpContext, selectedMethod.Id, CheckoutPaymentStatus.Failed, validation.PaymentReference, currentSignature);
                Input.SelectedMethodId = selectedMethod.Id;
                Input.CartSignature = currentSignature;
                ModelState.AddModelError(string.Empty, "Cart changed because of price or stock updates. Review totals and retry payment.");
                return Page();
            }

            var updatedState = _checkoutStateService.SavePaymentSelection(HttpContext, selectedMethod.Id, validation.Status, validation.PaymentReference, currentSignature);
            PaymentStatus = validation.Status;
            PaymentReference = validation.PaymentReference;
            Input.SelectedMethodId = selectedMethod.Id;
            Input.CartSignature = currentSignature;

            if (validation.Status == CheckoutPaymentStatus.Confirmed)
            {
                StoreSnapshot(quote);
                return await FinalizeOrderAsync(quote, updatedState);
            }

            if (validation.Status == CheckoutPaymentStatus.Canceled)
            {
                TempData["PaymentMessage"] = "Payment was cancelled. You can choose another method.";
                return Page();
            }

            await RecordFailedPaymentAsync(quote, updatedState, ResolvePaymentLabel(selectedMethod.Id));
            var error = PaymentStatusMapper.BuildBuyerMessage(PaymentStatuses.Failed);
            if (string.IsNullOrWhiteSpace(error))
            {
                error = "Payment authorization failed. Please try again or choose another method.";
            }
            ModelState.AddModelError(string.Empty, error);
            return Page();
        }

        private List<PaymentMethodOption> GetEnabledMethods()
        {
            return (_checkoutOptions.PaymentMethods ?? new List<PaymentMethodOption>())
                .Where(m => m != null && m.Enabled)
                .ToList();
        }

        private async Task<string> ResolveCurrencyAsync()
        {
            try
            {
                var region = new RegionInfo(CultureInfo.CurrentCulture.LCID);
                return await _currencyConfiguration.ResolveTransactionCurrencyAsync(region.ISOCurrencySymbol, HttpContext.RequestAborted);
            }
            catch
            {
                return await _currencyConfiguration.ResolveTransactionCurrencyAsync("USD", HttpContext.RequestAborted);
            }
        }

        private async Task RecordFailedPaymentAsync(ShippingQuote quote, CheckoutState state, string? paymentLabel)
        {
            ApplicationUser? buyer = null;
            if (User?.Identity?.IsAuthenticated ?? false)
            {
                buyer = await _userManager.GetUserAsync(User);
            }

            await _orderService.EnsureOrderAsync(
                state,
                quote,
                state.Address!,
                buyer?.Id,
                buyer?.Email,
                buyer?.FullName ?? buyer?.UserName,
                paymentLabel ?? state.PaymentMethod,
                state.PaymentMethod,
                OrderStatuses.Failed,
                PaymentStatuses.Failed,
                PaymentStatusMapper.BuildBuyerMessage(PaymentStatuses.Failed),
                0,
                cancellationToken: HttpContext.RequestAborted);
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

        private static string GenerateReference(string methodId)
        {
            return $"{methodId}-{Guid.NewGuid():N}".ToLowerInvariant();
        }

        private void StoreSnapshot(ShippingQuote quote)
        {
            var items = new List<CheckoutOrderItemSnapshot>();
            foreach (var group in quote.Summary.SellerGroups)
            {
                foreach (var item in group.Items)
                {
                    items.Add(new CheckoutOrderItemSnapshot(
                        item.Product.Id,
                        item.Quantity,
                        item.UnitPrice,
                        item.LineTotal,
                        BuildVariantKey(item.VariantAttributes)));
                }
            }

            var snapshot = new CheckoutOrderSnapshot(
                items,
                quote.Summary.ItemsSubtotal,
                quote.Summary.ShippingTotal,
                quote.Summary.GrandTotal,
                quote.Summary.TotalQuantity,
                quote.Summary.DiscountTotal,
                quote.Summary.AppliedPromoCode);
            TempData["CheckoutSnapshot"] = JsonSerializer.Serialize(snapshot, _serializerOptions);
        }

        private static string BuildVariantKey(IReadOnlyDictionary<string, string> attributes)
        {
            if (attributes == null || attributes.Count == 0)
            {
                return string.Empty;
            }

            var ordered = attributes.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase);
            return string.Join("|", ordered.Select(kv => $"{kv.Key}:{kv.Value}"));
        }

        private static string ComputeQuoteSignature(ShippingQuote quote)
        {
            var builder = new StringBuilder();
            foreach (var group in quote.Summary.SellerGroups.OrderBy(g => g.SellerId, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append($"seller:{group.SellerId};ship:{group.Shipping.ToString(CultureInfo.InvariantCulture)};");
                foreach (var item in group.Items.OrderBy(i => i.Product.Id).ThenBy(i => i.VariantLabel, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append($"p:{item.Product.Id};q:{item.Quantity};u:{item.UnitPrice.ToString(CultureInfo.InvariantCulture)};v:{BuildVariantKey(item.VariantAttributes)}|");
                }
            }

            builder.Append($"items:{quote.Summary.ItemsSubtotal.ToString(CultureInfo.InvariantCulture)};shiptotal:{quote.Summary.ShippingTotal.ToString(CultureInfo.InvariantCulture)};discount:{quote.Summary.DiscountTotal.ToString(CultureInfo.InvariantCulture)};grand:{quote.Summary.GrandTotal.ToString(CultureInfo.InvariantCulture)};qty:{quote.Summary.TotalQuantity};promo:{quote.Summary.AppliedPromoCode}");
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }

        private async Task<IActionResult> FinalizeOrderAsync(ShippingQuote quote, CheckoutState state)
        {
            var paymentLabel = ResolvePaymentLabel(state.PaymentMethod);
            ApplicationUser? buyer = null;
            if (User?.Identity?.IsAuthenticated ?? false)
            {
                buyer = await _userManager.GetUserAsync(User);
            }

            var result = await _orderService.EnsureOrderAsync(
                state,
                quote,
                state.Address!,
                buyer?.Id,
                buyer?.Email,
                buyer?.FullName ?? buyer?.UserName,
                paymentLabel,
                state.PaymentMethod,
                OrderStatuses.Paid,
                cancellationToken: HttpContext.RequestAborted);

            _cartService.ReplaceCart(HttpContext, Array.Empty<CartItem>());
            if (buyer != null)
            {
                await _userCartService.PersistAuthenticatedCartAsync(HttpContext, HttpContext.RequestAborted);
            }

            _promoCodeService.Clear(HttpContext);
            _checkoutStateService.Clear(HttpContext);
            return RedirectToPage("/Checkout/Confirmation", new { orderId = result.Order.Id });
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

        private async Task<bool> EnsurePaymentIntegrationEnabledAsync()
        {
            var availability = await _integrationService.EnsureEnabledAsync(IntegrationManagementService.PaymentIntegrationKey, HttpContext.RequestAborted);
            if (!availability.Allowed)
            {
                ModelState.AddModelError(string.Empty, availability.Message ?? "Payments are temporarily unavailable.");
                return false;
            }

            return true;
        }
    }

    public class PaymentInput
    {
        public string SelectedMethodId { get; set; } = string.Empty;

        public string CartSignature { get; set; } = string.Empty;

        public string? BlikCode { get; set; }
    }
}
