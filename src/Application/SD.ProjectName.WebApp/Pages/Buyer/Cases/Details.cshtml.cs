using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Buyer.Cases
{
    [Authorize(Roles = AccountTypes.Buyer)]
    public class DetailsModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;

        [BindProperty(SupportsGet = true)]
        public string? CaseId { get; set; }

        public BuyerCaseDetailView? Case { get; private set; }

        public bool HasMessages => Case?.Messages != null && Case.Messages.Count > 0;

        [BindProperty]
        public string? MessageBody { get; set; }

        public DetailsModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync(string? caseId)
        {
            CaseId = string.IsNullOrWhiteSpace(caseId) ? CaseId : caseId;
            return await LoadAsync();
        }

        public async Task<IActionResult> OnPostMessageAsync()
        {
            var buyerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(buyerId))
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
                return await LoadAsync();
            }

            var result = await _orderService.AddReturnCaseMessageForBuyerAsync(
                buyerId,
                CaseId!,
                MessageBody!,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(nameof(MessageBody), result.Error ?? "Unable to send message.");
                return await LoadAsync();
            }

            return RedirectToPage(new { caseId = CaseId });
        }

        private async Task<IActionResult> LoadAsync()
        {
            var buyerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return Challenge();
            }

            if (string.IsNullOrWhiteSpace(CaseId))
            {
                return NotFound();
            }

            Case = await _orderService.GetReturnCaseForBuyerAsync(buyerId, CaseId, HttpContext.RequestAborted);
            if (Case == null)
            {
                return NotFound();
            }

            return Page();
        }
    }
}
