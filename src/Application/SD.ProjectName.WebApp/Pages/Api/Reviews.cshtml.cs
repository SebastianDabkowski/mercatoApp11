using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Api
{
    [IgnoreAntiforgeryToken]
    public class ReviewsModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;

        public ReviewsModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnPostReportAsync([FromBody] ReviewReportRequest request)
        {
            if (request == null || request.ReviewId <= 0)
            {
                return BadRequest(new { message = "Select a review to report." });
            }

            if (User?.Identity?.IsAuthenticated != true)
            {
                return new JsonResult(new { message = "Sign in to report reviews." })
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
            }

            var userId = _userManager.GetUserId(User);
            var result = await _orderService.ReportReviewAsync(request.ReviewId, userId, request.Reason, request.Details, HttpContext.RequestAborted);
            if (!result.Success)
            {
                return BadRequest(new { message = result.Error ?? "Could not report this review." });
            }

            return new JsonResult(new { success = true, message = "Thanks for letting us know. We will review this shortly." });
        }
    }

    public class ReviewReportRequest
    {
        public int ReviewId { get; set; }

        public string Reason { get; set; } = string.Empty;

        public string? Details { get; set; }
    }
}
