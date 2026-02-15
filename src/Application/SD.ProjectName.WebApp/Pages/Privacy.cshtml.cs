using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages
{
    public class PrivacyModel : PageModel
    {
        private readonly ILogger<PrivacyModel> _logger;

        public PrivacyModel(ILogger<PrivacyModel> logger)
        {
            _logger = logger;
        }

        public IActionResult OnGet()
        {
            _logger.LogInformation("Redirecting /Privacy to legal content page.");
            return RedirectToPage("/Legal/Document", new { type = LegalDocumentTypes.PrivacyPolicy });
        }
    }

}
