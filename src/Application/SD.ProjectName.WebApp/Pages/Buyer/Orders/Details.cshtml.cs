using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Buyer.Orders
{
    [Authorize(Policy = Permissions.BuyerPortal)]
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

        [BindProperty]
        public string? ReturnType { get; set; } = ReturnRequestTypes.Return;

        [BindProperty]
        public string? ReturnDescription { get; set; }

        [BindProperty]
        public int ReviewProductId { get; set; }

        [BindProperty]
        public int ReviewRating { get; set; } = 5;

        [BindProperty]
        public string? ReviewContent { get; set; }

        [BindProperty]
        public string? SellerRatingSellerId { get; set; }

        [BindProperty]
        public int SellerRatingValue { get; set; } = 5;

        [BindProperty]
        public string? MessageSubOrder { get; set; }

        [BindProperty]
        public string? MessageContent { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public DetailsModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public OrderView? Order { get; private set; }
        public int ReturnWindowDays => ReturnPolicies.ReturnWindowDays;
        public bool CanSubmitReview => Order != null && string.Equals(OrderStatuses.Normalize(Order.Status), OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase);
        public Dictionary<string, int> SellerRatings { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public async Task<IActionResult> OnGetAsync(int orderId)
        {
            var buyerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return Challenge();
            }

            return await LoadAsync(orderId, buyerId);
        }

        public string? BuildTrackingLink(string? trackingNumber, string? trackingCarrier)
        {
            if (string.IsNullOrWhiteSpace(trackingNumber))
            {
                return null;
            }

            var trimmed = trackingNumber.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return uri.ToString();
            }

            if (string.IsNullOrWhiteSpace(trackingCarrier))
            {
                return null;
            }

            var carrier = trackingCarrier.Trim().ToLowerInvariant();
            var encodedTracking = Uri.EscapeDataString(trimmed);

            return carrier switch
            {
                "ups" or "ups ground" => $"https://www.ups.com/track?tracknum={encodedTracking}",
                "fedex" or "fed ex" => $"https://www.fedex.com/fedextrack/?trknbr={encodedTracking}",
                "usps" or "postal service" or "united states postal service" => $"https://tools.usps.com/go/TrackConfirmAction?qtc_tLabels1={encodedTracking}",
                "dhl" or "dhl express" => $"https://www.dhl.com/en/express/tracking.html?AWB={encodedTracking}",
                _ => null
            };
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

            if (subOrder.Return != null && ReturnRequestStatuses.IsOpen(subOrder.Return.Status))
            {
                return false;
            }

            return true;
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
                ModelState.AddModelError(nameof(ReturnSubOrder), "Select a sub-order to request support for.");
            }

            if (string.IsNullOrWhiteSpace(ReturnReason))
            {
                ModelState.AddModelError(nameof(ReturnReason), "Provide a reason for the request.");
            }

            if (string.IsNullOrWhiteSpace(ReturnType) || !ReturnRequestTypes.IsSupported(ReturnType))
            {
                ModelState.AddModelError(nameof(ReturnType), "Select return or complaint.");
            }

            if (string.IsNullOrWhiteSpace(ReturnDescription))
            {
                ModelState.AddModelError(nameof(ReturnDescription), "Provide a description of the issue.");
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
                ReturnType,
                ReturnDescription,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to submit return request.");
                return Page();
            }

            StatusMessage = result.Request == null
                ? "Case submitted."
                : $"Case {result.Request.CaseId} submitted. Status: {result.Request.Status}.";
            return RedirectToPage(new { orderId });
        }

        public async Task<IActionResult> OnPostReviewAsync(int orderId)
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

            if (!CanSubmitReview)
            {
                ModelState.AddModelError(string.Empty, "Reviews are available after delivery.");
            }

            if (ReviewProductId <= 0 || Order?.Items.All(i => i.ProductId != ReviewProductId) != false)
            {
                ModelState.AddModelError(nameof(ReviewProductId), "Select a product from this order.");
            }

            if (ReviewRating < 1 || ReviewRating > 5)
            {
                ModelState.AddModelError(nameof(ReviewRating), "Rating must be between 1 and 5.");
            }

            if (string.IsNullOrWhiteSpace(ReviewContent))
            {
                ModelState.AddModelError(nameof(ReviewContent), "Share feedback for your review.");
            }
            else if (ReviewContent!.Length > 2000)
            {
                ModelState.AddModelError(nameof(ReviewContent), "Review text must be 2000 characters or fewer.");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _orderService.SubmitProductReviewAsync(
                orderId,
                ReviewProductId,
                buyerId,
                null,
                ReviewRating,
                ReviewContent,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to submit review.");
                return Page();
            }

            var requiresModeration = result.Review != null && !ReviewStatuses.IsVisible(result.Review.Status);
            StatusMessage = requiresModeration
                ? "Review submitted for moderation. It will appear once approved."
                : "Review submitted. Thank you for your feedback!";
            return RedirectToPage(new { orderId });
        }

        public async Task<IActionResult> OnPostSellerRatingAsync(int orderId)
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

            if (!CanSubmitReview)
            {
                ModelState.AddModelError(string.Empty, "Ratings are available after delivery.");
            }

            if (string.IsNullOrWhiteSpace(SellerRatingSellerId) || Order?.SubOrders.All(s => !string.Equals(s.SellerId, SellerRatingSellerId, StringComparison.OrdinalIgnoreCase)) != false)
            {
                ModelState.AddModelError(nameof(SellerRatingSellerId), "Select a seller from this order.");
            }
            else if (SellerRatings.ContainsKey(SellerRatingSellerId))
            {
                ModelState.AddModelError(nameof(SellerRatingSellerId), "You already rated this seller.");
            }

            if (SellerRatingValue < 1 || SellerRatingValue > 5)
            {
                ModelState.AddModelError(nameof(SellerRatingValue), "Rating must be between 1 and 5.");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _orderService.SubmitSellerRatingAsync(
                orderId,
                SellerRatingSellerId!,
                buyerId,
                SellerRatingValue,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to submit rating.");
                return Page();
            }

            StatusMessage = "Seller rating submitted. Thank you for your feedback!";
            return RedirectToPage(new { orderId });
        }

        public async Task<IActionResult> OnPostMessageAsync(int orderId)
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

            if (string.IsNullOrWhiteSpace(MessageSubOrder))
            {
                ModelState.AddModelError(nameof(MessageSubOrder), "Select a seller to message.");
            }

            if (string.IsNullOrWhiteSpace(MessageContent))
            {
                ModelState.AddModelError(nameof(MessageContent), "Enter a message.");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _orderService.AddOrderMessageAsync(
                orderId,
                MessageSubOrder!,
                OrderMessageRoles.Buyer,
                buyerId,
                MessageContent!,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to send your message.");
                return Page();
            }

            StatusMessage = "Message sent to the seller.";
            return RedirectToPage(new { orderId });
        }

        private async Task<IActionResult> LoadAsync(int orderId, string buyerId)
        {
            Order = await _orderService.GetOrderAsync(orderId, buyerId, HttpContext.RequestAborted);
            if (Order == null)
            {
                return NotFound();
            }

            SellerRatings = await _orderService.GetSellerRatingsForOrderAsync(orderId, buyerId, HttpContext.RequestAborted);

            if (string.IsNullOrWhiteSpace(SellerRatingSellerId))
            {
                var nextSeller = Order.SubOrders
                    .Where(s => string.Equals(OrderStatuses.Normalize(s.Status), OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.SellerId)
                    .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id) && !SellerRatings.ContainsKey(id));

                SellerRatingSellerId = nextSeller;
            }

            if (string.IsNullOrWhiteSpace(ReturnSubOrder))
            {
                ReturnSubOrder = Order.SubOrders.FirstOrDefault(CanRequestReturn)?.SubOrderNumber;
            }

            if (string.IsNullOrWhiteSpace(ReturnType))
            {
                ReturnType = ReturnRequestTypes.Return;
            }

            if (ReviewProductId <= 0 && Order.Items.Count > 0)
            {
                ReviewProductId = Order.Items[0].ProductId;
            }

            if (string.IsNullOrWhiteSpace(MessageSubOrder))
            {
                MessageSubOrder = Order.SubOrders.FirstOrDefault()?.SubOrderNumber;
            }

            return Page();
        }
    }
}
