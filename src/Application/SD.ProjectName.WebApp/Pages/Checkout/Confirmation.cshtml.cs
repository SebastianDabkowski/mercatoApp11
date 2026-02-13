using System.Text.Json;
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
        private readonly OrderService _orderService;
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public CartSummary Summary { get; private set; } = CartSummary.Empty;

        public DeliveryAddress? Address { get; private set; }

        public string? PaymentMethodLabel { get; private set; }

        public string? PaymentReference { get; private set; }

        public OrderView? Order { get; private set; }

        public ConfirmationModel(
            CartViewService cartViewService,
            IUserCartService userCartService,
            CheckoutStateService checkoutStateService,
            ShippingOptionsService shippingOptionsService,
            CheckoutOptions checkoutOptions,
            UserManager<ApplicationUser> userManager,
            OrderService orderService)
        {
            _cartViewService = cartViewService;
            _userCartService = userCartService;
            _checkoutStateService = checkoutStateService;
            _shippingOptionsService = shippingOptionsService;
            _checkoutOptions = checkoutOptions;
            _userManager = userManager;
            _orderService = orderService;
        }

        public async Task<IActionResult> OnGetAsync(int? orderId = null)
        {
            if (orderId.HasValue)
            {
                var currentUserId = _userManager.GetUserId(User);
                var order = await _orderService.GetOrderAsync(orderId.Value, currentUserId, HttpContext.RequestAborted);
                if (order == null)
                {
                    return NotFound();
                }

                PopulateFromOrder(order);
                _checkoutStateService.Clear(HttpContext);
                return Page();
            }

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

            var snapshot = ReadSnapshot();
            if (snapshot != null)
            {
                Summary = new CartSummary(
                    new List<CartSellerGroup>(),
                    snapshot.ItemsSubtotal,
                    snapshot.ShippingTotal,
                    snapshot.GrandTotal,
                    snapshot.TotalQuantity,
                    CartSettlementSummary.Empty,
                    snapshot.DiscountTotal,
                    snapshot.PromoCode);
            }
            else
            {
                var sellerCountries = await LoadSellerCountriesAsync(summary.SellerGroups.Select(g => g.SellerId));
                var quote = _shippingOptionsService.BuildQuote(summary, state.Address, sellerCountries, state.ShippingSelections);
                Summary = quote.Summary;
            }

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

        private CheckoutOrderSnapshot? ReadSnapshot()
        {
            if (!TempData.TryGetValue("CheckoutSnapshot", out var raw) || raw is not string payload || string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<CheckoutOrderSnapshot>(payload, _serializerOptions);
            }
            catch
            {
                return null;
            }
        }

        private void PopulateFromOrder(OrderView order)
        {
            Order = order;
            Address = order.Address;
            PaymentReference = order.PaymentReference;
            PaymentMethodLabel = order.PaymentMethodLabel;
            Summary = new CartSummary(
                new List<CartSellerGroup>(),
                order.ItemsSubtotal,
                order.ShippingTotal,
                order.GrandTotal,
                order.TotalQuantity,
                CartSettlementSummary.Empty,
                order.DiscountTotal,
                order.PromoCode);
        }
    }
}
