using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Cases
{
    [Authorize(Policy = Permissions.SupportCases)]
    public class DetailsModel : PageModel
    {
        private readonly OrderService _orderService;

        [BindProperty(SupportsGet = true)]
        public string? CaseId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int OrderId { get; set; }

        [BindProperty]
        public string? EscalationReason { get; set; }

        [BindProperty]
        public string? Decision { get; set; }

        [BindProperty]
        public decimal? RefundAmount { get; set; }

        [BindProperty]
        public string? RefundReference { get; set; }

        [BindProperty]
        public string? Note { get; set; }

        public AdminCaseDetailView? Case { get; private set; }

        public bool CanEscalate =>
            Case != null
            && !string.Equals(ReturnRequestStatuses.Normalize(Case.Summary.Status), ReturnRequestStatuses.UnderAdminReview, StringComparison.OrdinalIgnoreCase);

        public DetailsModel(OrderService orderService)
        {
            _orderService = orderService;
        }

        public async Task<IActionResult> OnGetAsync(string? caseId, int? orderId)
        {
            CaseId = string.IsNullOrWhiteSpace(caseId) ? CaseId : caseId;
            if (orderId.HasValue)
            {
                OrderId = orderId.Value;
            }

            if (string.IsNullOrWhiteSpace(CaseId))
            {
                return NotFound();
            }

            return await LoadAsync(CaseId);
        }

        public async Task<IActionResult> OnPostEscalateAsync()
        {
            if (string.IsNullOrWhiteSpace(CaseId))
            {
                return NotFound();
            }

            var result = await _orderService.EscalateReturnCaseForAdminAsync(
                OrderId,
                CaseId!,
                EscalationReason,
                Note,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to escalate case.");
                return await LoadAsync(CaseId);
            }

            return RedirectToPage(new { caseId = CaseId, orderId = OrderId });
        }

        public async Task<IActionResult> OnPostDecisionAsync()
        {
            if (string.IsNullOrWhiteSpace(CaseId))
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(Decision))
            {
                ModelState.AddModelError(nameof(Decision), "Select an action.");
                return await LoadAsync(CaseId);
            }

            var normalizedDecision = Decision!.Trim().ToLowerInvariant();
            if ((normalizedDecision == "partialrefund"
                || normalizedDecision == "partial_refund"
                || normalizedDecision == "partial")
                && (!RefundAmount.HasValue || RefundAmount <= 0))
            {
                ModelState.AddModelError(nameof(RefundAmount), "Enter a refund amount for partial refunds.");
                return await LoadAsync(CaseId);
            }

            var result = await _orderService.ResolveReturnCaseForAdminAsync(
                OrderId,
                CaseId!,
                Decision!,
                RefundAmount,
                RefundReference,
                Note,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to record decision.");
                return await LoadAsync(CaseId);
            }

            return RedirectToPage(new { caseId = CaseId, orderId = OrderId });
        }

        private async Task<IActionResult> LoadAsync(string caseId)
        {
            var detail = await _orderService.GetReturnCaseForAdminAsync(caseId, HttpContext.RequestAborted);
            if (detail == null)
            {
                return NotFound();
            }

            Case = detail;
            OrderId = detail.Summary.OrderId;
            return Page();
        }
    }
}
