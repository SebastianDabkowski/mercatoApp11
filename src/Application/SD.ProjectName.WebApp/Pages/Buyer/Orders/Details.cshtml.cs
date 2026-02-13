using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Buyer.Orders
{
    [Authorize(Roles = AccountTypes.Buyer)]
    public class DetailsModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;

        public DetailsModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public OrderView? Order { get; private set; }

        public async Task<IActionResult> OnGetAsync(int orderId)
        {
            var buyerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return Challenge();
            }

            Order = await _orderService.GetOrderAsync(orderId, buyerId, HttpContext.RequestAborted);
            if (Order == null)
            {
                return NotFound();
            }

            return Page();
        }

        public string? BuildTrackingLink(string? trackingNumber)
        {
            if (string.IsNullOrWhiteSpace(trackingNumber))
            {
                return null;
            }

            return Uri.TryCreate(trackingNumber.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.ToString()
                : null;
        }
    }
}
