using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Api.Payments
{
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public class WebhookModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly ILogger<WebhookModel> _logger;

        public WebhookModel(OrderService orderService, ILogger<WebhookModel> logger)
        {
            _orderService = orderService;
            _logger = logger;
        }

        public async Task<IActionResult> OnPostAsync([FromBody] PaymentWebhookRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Reference) || string.IsNullOrWhiteSpace(request.Status))
            {
                return BadRequest(new { message = "Invalid payload." });
            }

            var update = await _orderService.UpdatePaymentStatusAsync(
                request.Reference,
                request.Status,
                request.RefundedAmount,
                null,
                HttpContext.RequestAborted);

            if (!update.Success)
            {
                _logger.LogWarning("Payment webhook failed for reference {Reference}: {Error}", request.Reference, update.Error);
                return NotFound(new { message = update.Error ?? "Order not found." });
            }

            return new JsonResult(new
            {
                status = update.PaymentStatus,
                refundedAmount = update.PaymentRefundedAmount
            });
        }
    }

    public record PaymentWebhookRequest(string Reference, string Status, decimal? RefundedAmount);
}
