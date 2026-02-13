using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller.Orders
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class DetailsModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;

        public DetailsModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public SellerOrderView? Order { get; private set; }

        public async Task<IActionResult> OnGetAsync(int orderId)
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            var order = await _orderService.GetSellerOrderAsync(orderId, sellerId, HttpContext.RequestAborted);
            if (order == null)
            {
                return NotFound();
            }

            Order = order;
            return Page();
        }
    }
}
