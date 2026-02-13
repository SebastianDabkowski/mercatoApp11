using System.Linq;
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

        [BindProperty]
        public string? ReturnSubOrder { get; set; }

        [BindProperty]
        public List<int> ReturnItems { get; set; } = new();

        [BindProperty]
        public string? ReturnReason { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public DetailsModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public OrderView? Order { get; private set; }
        public int ReturnWindowDays => ReturnPolicies.ReturnWindowDays;

        public async Task<IActionResult> OnGetAsync(int orderId)
        {
            var buyerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return Challenge();
            }

            return await LoadAsync(orderId, buyerId);
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

        public bool CanRequestReturn(OrderSubOrder subOrder)
        {
            if (Order == null)
            {
                return false;
            }

            if (!string.Equals(OrderStatuses.Normalize(subOrder.Status), OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (subOrder.Return != null)
            {
                return false;
            }

            return OrderService.IsReturnWindowOpen(subOrder, Order.CreatedOn);
        }

        public bool IsReturnWindowOpen(OrderSubOrder subOrder)
        {
            return Order != null && OrderService.IsReturnWindowOpen(subOrder, Order.CreatedOn);
        }

        public async Task<IActionResult> OnPostReturnAsync(int orderId)
        {
            var buyerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return Challenge();
            }

            var loadResult = await LoadAsync(orderId, buyerId);
            if (loadResult is not PageResult)
            {
                return loadResult;
            }

            if (string.IsNullOrWhiteSpace(ReturnSubOrder))
            {
                ModelState.AddModelError(nameof(ReturnSubOrder), "Select a sub-order to return.");
            }

            if (string.IsNullOrWhiteSpace(ReturnReason))
            {
                ModelState.AddModelError(nameof(ReturnReason), "Provide a reason for the return.");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _orderService.CreateReturnRequestAsync(
                orderId,
                buyerId,
                ReturnSubOrder,
                ReturnItems,
                ReturnReason,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to submit return request.");
                return Page();
            }

            StatusMessage = "Return request submitted.";
            return RedirectToPage(new { orderId });
        }

        private async Task<IActionResult> LoadAsync(int orderId, string buyerId)
        {
            Order = await _orderService.GetOrderAsync(orderId, buyerId, HttpContext.RequestAborted);
            if (Order == null)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(ReturnSubOrder))
            {
                ReturnSubOrder = Order.SubOrders.FirstOrDefault(CanRequestReturn)?.SubOrderNumber;
            }

            return Page();
        }
    }
}
