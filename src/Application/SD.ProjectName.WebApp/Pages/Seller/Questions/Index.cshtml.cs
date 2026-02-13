using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller.Questions
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class IndexModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public IReadOnlyList<ProductQuestionView> Questions { get; private set; } = Array.Empty<ProductQuestionView>();

        [BindProperty]
        public int QuestionId { get; set; }

        [BindProperty]
        public string? Answer { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            await LoadAsync(sellerId);
            return Page();
        }

        public async Task<IActionResult> OnPostAnswerAsync()
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            if (QuestionId <= 0)
            {
                ModelState.AddModelError(string.Empty, "Select a question to answer.");
            }

            if (string.IsNullOrWhiteSpace(Answer))
            {
                ModelState.AddModelError(nameof(Answer), "Enter an answer.");
            }

            await LoadAsync(sellerId);
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _orderService.AnswerProductQuestionAsync(
                QuestionId,
                sellerId,
                Answer!,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to send your answer.");
                return Page();
            }

            StatusMessage = "Answer sent.";
            return RedirectToPage();
        }

        private async Task LoadAsync(string sellerId)
        {
            Questions = await _orderService.GetProductQuestionsForSellerAsync(sellerId, HttpContext.RequestAborted);
        }
    }
}
