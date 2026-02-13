using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Api
{
    [IgnoreAntiforgeryToken]
    public class PromoModel : PageModel
    {
        private readonly IUserCartService _userCartService;
        private readonly CartViewService _cartViewService;
        private readonly PromoCodeService _promoCodeService;

        public PromoModel(IUserCartService userCartService, CartViewService cartViewService, PromoCodeService promoCodeService)
        {
            _userCartService = userCartService;
            _cartViewService = cartViewService;
            _promoCodeService = promoCodeService;
        }

        public async Task<IActionResult> OnPostApplyAsync([FromBody] ApplyPromoRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest(new { message = "Enter a promo code." });
            }

            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);
            var summary = await _cartViewService.BuildAsync(HttpContext);
            if (summary.IsEmpty)
            {
                return BadRequest(new { message = "Your cart is empty." });
            }

            var result = _promoCodeService.Apply(HttpContext, summary, request.Code);
            var status = result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;

            return new JsonResult(new PromoApplyResponse(result.Success, result.Message, result.AlreadyApplied, ToTotals(result.Summary), result.AppliedCode))
            {
                StatusCode = status
            };
        }

        public async Task<IActionResult> OnPostRemoveAsync()
        {
            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);
            _promoCodeService.Clear(HttpContext);

            var summary = await _cartViewService.BuildAsync(HttpContext);
            return new JsonResult(new PromoApplyResponse(true, "Promo code removed.", false, ToTotals(summary), summary.AppliedPromoCode));
        }

        private static CartTotalsDto ToTotals(CartSummary summary)
        {
            return new CartTotalsDto(
                summary.ItemsSubtotal,
                summary.ShippingTotal,
                summary.GrandTotal,
                summary.TotalQuantity,
                summary.SellerGroups.Select(g => new CartSellerTotalsDto(g.SellerId, g.Subtotal, g.Shipping, g.Total)).ToList(),
                summary.DiscountTotal,
                summary.AppliedPromoCode);
        }
    }

    public record ApplyPromoRequest(string Code);

    public record PromoApplyResponse(bool Success, string Message, bool AlreadyApplied, CartTotalsDto Totals, string? PromoCode);
}
