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

        [BindProperty]
        public string? NewStatus { get; set; }

        [BindProperty]
        public string? TrackingNumber { get; set; }

        [BindProperty]
        public string? TrackingCarrier { get; set; }

        [BindProperty]
        public decimal? RefundedAmount { get; set; }

        [BindProperty]
        public List<int> SelectedItems { get; set; } = new();

        public DetailsModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public SellerOrderView? Order { get; private set; }
        public List<string> NextStatuses { get; private set; } = new();

        public async Task<IActionResult> OnGetAsync(int orderId)
        {
            return await LoadAsync(orderId);
        }

        public async Task<IActionResult> OnGetLabelAsync(int orderId)
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            var label = await _orderService.GetShippingLabelAsync(orderId, sellerId, HttpContext.RequestAborted);
            if (label == null)
            {
                return NotFound();
            }

            return File(label.Content, label.ContentType, label.FileName);
        }

        public async Task<IActionResult> OnPostStatusAsync(int orderId)
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            if (string.IsNullOrWhiteSpace(NewStatus))
            {
                ModelState.AddModelError(nameof(NewStatus), "Select a status to update.");
                return await LoadAsync(orderId);
            }

            var result = await _orderService.UpdateSubOrderStatusAsync(
                orderId,
                sellerId,
                NewStatus!,
                TrackingNumber,
                RefundedAmount,
                TrackingCarrier,
                SelectedItems,
                shippingProviderReference: null,
                cancellationToken: HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to update status.");
                return await LoadAsync(orderId);
            }

            return RedirectToPage(new { orderId });
        }

        private async Task<IActionResult> LoadAsync(int orderId)
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            var order = await _orderService.GetSellerOrderAsync(orderId, sellerId, HttpContext.RequestAborted);
            if (order == null)
            {
                var exists = await _orderService.OrderExistsAsync(orderId, HttpContext.RequestAborted);
                return exists ? Forbid() : NotFound();
            }

            Order = order;
            NextStatuses = OrderStatuses.NextStatuses(order.Status);
            TrackingNumber = order.TrackingNumber;
            TrackingCarrier = order.TrackingCarrier;
            return Page();
        }
    }
}
