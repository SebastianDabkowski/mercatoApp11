using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Questions
{
    [Authorize(Policy = Permissions.SupportQuestions)]
    public class IndexModel : PageModel
    {
        private readonly OrderService _orderService;

        public IndexModel(OrderService orderService)
        {
            _orderService = orderService;
        }

        public IReadOnlyList<ProductQuestionView> ProductQuestions { get; private set; } = Array.Empty<ProductQuestionView>();

        public IReadOnlyList<OrderMessageAdminView> OrderMessages { get; private set; } = Array.Empty<OrderMessageAdminView>();

        [BindProperty]
        public int QuestionId { get; set; }

        [BindProperty]
        public string? QuestionStatus { get; set; }

        [BindProperty]
        public int MessageOrderId { get; set; }

        [BindProperty]
        public Guid MessageId { get; set; }

        [BindProperty]
        public bool HideMessage { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostQuestionStatusAsync()
        {
            if (QuestionId <= 0)
            {
                ModelState.AddModelError(string.Empty, "Select a question to update.");
            }

            if (string.IsNullOrWhiteSpace(QuestionStatus))
            {
                ModelState.AddModelError(nameof(QuestionStatus), "Select a status.");
            }

            await LoadAsync();
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _orderService.UpdateProductQuestionStatusAsync(
                QuestionId,
                QuestionStatus!,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to update question.");
                return Page();
            }

            StatusMessage = "Question updated.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostMessageVisibilityAsync()
        {
            if (MessageOrderId <= 0 || MessageId == Guid.Empty)
            {
                ModelState.AddModelError(string.Empty, "Select a message to update.");
            }

            await LoadAsync();
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _orderService.SetOrderMessageVisibilityAsync(
                MessageOrderId,
                MessageId,
                HideMessage,
                HttpContext.RequestAborted);

            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Unable to update message.");
                return Page();
            }

            StatusMessage = HideMessage ? "Message hidden." : "Message restored.";
            return RedirectToPage();
        }

        private async Task LoadAsync()
        {
            ProductQuestions = await _orderService.GetRecentProductQuestionsAsync(50, HttpContext.RequestAborted);
            OrderMessages = await _orderService.GetRecentOrderMessagesForAdminAsync(50, HttpContext.RequestAborted);
        }
    }
}
