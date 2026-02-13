using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller.Cases
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class DetailsModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;

        [BindProperty(SupportsGet = true)]
        public string? CaseId { get; set; }

        [BindProperty]
        public string? Decision { get; set; }

        [BindProperty]
        public string? Note { get; set; }

        [BindProperty]
        public string? MessageBody { get; set; }

        [BindProperty]
        public int OrderId { get; set; }

        public SellerCaseDetailView? Case { get; private set; }

        public bool CanReview =>
            Case != null
            && string.Equals(ReturnRequestStatuses.PendingSellerReview, ReturnRequestStatuses.Normalize(Case.Summary.Status), StringComparison.OrdinalIgnoreCase);

        public DetailsModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync(string? caseId)
        {
            CaseId = string.IsNullOrWhiteSpace(caseId) ? CaseId : caseId;
            if (string.IsNullOrWhiteSpace(CaseId))
            {
                return NotFound();
            }

            return await LoadAsync(CaseId);
        }

        public async Task<IActionResult> OnPostMessageAsync()
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            if (string.IsNullOrWhiteSpace(CaseId))
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(MessageBody))
            {
                ModelState.AddModelError(nameof(MessageBody), "Enter a message.");
                return await LoadAsync(CaseId);
            }

            var result = await _orderService.AddReturnCaseMessageForSellerAsync(
                sellerId,
                CaseId!,
                MessageBody!,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(nameof(MessageBody), result.Error ?? "Unable to send message.");
                return await LoadAsync(CaseId);
            }

            return RedirectToPage(new { caseId = CaseId });
        }

        public async Task<IActionResult> OnPostDecisionAsync()
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            if (string.IsNullOrWhiteSpace(CaseId))
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(Decision))
            {
                ModelState.AddModelError(nameof(Decision), "Select an action.");
                return await LoadAsync(CaseId);
            }

            var result = await _orderService.UpdateReturnCaseForSellerAsync(
                OrderId,
                sellerId,
                CaseId,
                Decision!,
                Note,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to update case.");
                return await LoadAsync(CaseId);
            }

            return RedirectToPage(new { caseId = CaseId });
        }

        private async Task<IActionResult> LoadAsync(string caseId)
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            var detail = await _orderService.GetReturnCaseForSellerAsync(sellerId, caseId, HttpContext.RequestAborted);
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
