using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller.Payouts
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class DetailsModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;

        public SellerPayoutDetailView? Payout { get; private set; }

        public DetailsModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync(int orderId)
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            var payout = await _orderService.GetSellerPayoutAsync(orderId, sellerId, HttpContext.RequestAborted);
            if (payout == null)
            {
                return NotFound();
            }

            Payout = payout;
            return Page();
        }
    }
}
