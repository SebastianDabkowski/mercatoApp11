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

        public DetailsModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync(string? caseId)
        {
            var buyerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return Challenge();
            }

            CaseId = string.IsNullOrWhiteSpace(caseId) ? CaseId : caseId;
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
