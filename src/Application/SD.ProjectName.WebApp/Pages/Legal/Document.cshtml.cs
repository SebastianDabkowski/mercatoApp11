using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Legal
{
    public class DocumentModel : PageModel
    {
        private readonly ILegalDocumentService _legalDocuments;

        public DocumentModel(ILegalDocumentService legalDocuments)
        {
            _legalDocuments = legalDocuments;
        }

        public string DocumentType { get; private set; } = LegalDocumentTypes.TermsOfService;

        public LegalDocumentVersion? ActiveVersion { get; private set; }

        public LegalDocumentVersion? UpcomingVersion { get; private set; }

        public string DisplayName => LegalDocumentTypes.GetDisplayName(DocumentType);

        public async Task OnGetAsync(string? type = null)
        {
            DocumentType = LegalDocumentTypes.Normalize(type);
            var now = DateTimeOffset.UtcNow;
            ActiveVersion = await _legalDocuments.GetActiveVersionAsync(DocumentType, now, HttpContext.RequestAborted);
            UpcomingVersion = await _legalDocuments.GetUpcomingVersionAsync(DocumentType, now, HttpContext.RequestAborted);
            ViewData["Title"] = DisplayName;
        }
    }
}
