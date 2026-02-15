using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Products
{
    [Authorize(Policy = Permissions.AdminModeration)]
    public class PhotoModerationModel : PageModel
    {
        private const int DefaultPageSize = 20;
        private readonly ProductPhotoModerationService _moderationService;

        public PhotoModerationModel(ProductPhotoModerationService moderationService)
        {
            _moderationService = moderationService;
        }

        [BindProperty(SupportsGet = true, Name = "status")]
        public List<string> Statuses { get; set; } = new() { ProductPhotoStatuses.Pending };

        [BindProperty(SupportsGet = true, Name = "flagged")]
        public bool FlaggedOnly { get; set; } = true;

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        [BindProperty]
        public List<int> SelectedIds { get; set; } = new();

        [BindProperty]
        [Display(Name = "Moderator note")]
        public string? ModerationNote { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public PagedResult<ProductPhotoModerationItem> Photos { get; private set; } = new()
        {
            Items = new List<ProductPhotoModerationItem>(),
            PageNumber = 1,
            PageSize = DefaultPageSize,
            TotalCount = 0
        };

        public Dictionary<int, List<ProductPhotoAuditView>> AuditTrail { get; private set; } = new();

        public List<string> AvailableStatuses { get; } = new()
        {
            ProductPhotoStatuses.Pending,
            ProductPhotoStatuses.Approved,
            ProductPhotoStatuses.Removed
        };

        public bool HasFilters => (Statuses?.Count ?? 0) > 0 || !FlaggedOnly;

        public async Task OnGetAsync()
        {
            await LoadAsync();
        }

        public async Task<IActionResult> OnPostApproveAsync()
        {
            var actor = GetActor();
            var result = await _moderationService.ApproveAsync(SelectedIds, actor, ModerationNote);
            StatusMessage = result.Success ? "Photos approved and restored where applicable." : (result.Error ?? "Unable to approve photos.");

            return RedirectToPage(new { status = Statuses, flagged = FlaggedOnly, page = PageNumber });
        }

        public async Task<IActionResult> OnPostRemoveAsync()
        {
            if (string.IsNullOrWhiteSpace(ModerationNote))
            {
                StatusMessage = "Provide a removal reason.";
                return RedirectToPage(new { status = Statuses, flagged = FlaggedOnly, page = PageNumber });
            }

            var actor = GetActor();
            var result = await _moderationService.RemoveAsync(SelectedIds, actor, ModerationNote);
            StatusMessage = result.Success ? "Photos removed and sellers notified." : (result.Error ?? "Unable to remove photos.");

            return RedirectToPage(new { status = Statuses, flagged = FlaggedOnly, page = PageNumber });
        }

        private async Task LoadAsync()
        {
            var filters = new ProductPhotoModerationFilters
            {
                Statuses = Statuses.Select(ProductPhotoStatuses.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                FlaggedOnly = FlaggedOnly
            };

            PageNumber = Math.Max(1, PageNumber);
            Photos = await _moderationService.GetQueueAsync(filters, PageNumber, DefaultPageSize, HttpContext.RequestAborted);
            PageNumber = Photos.PageNumber <= 0 ? 1 : Photos.PageNumber;

            await LoadAuditTrailAsync(Photos.Items);
        }

        private async Task LoadAuditTrailAsync(IEnumerable<ProductPhotoModerationItem> items)
        {
            AuditTrail = new Dictionary<int, List<ProductPhotoAuditView>>();
            foreach (var item in items)
            {
                var audit = await _moderationService.GetAuditAsync(item.Id, HttpContext.RequestAborted);
                AuditTrail[item.Id] = audit;
            }
        }

        private string GetActor() => string.IsNullOrWhiteSpace(User?.Identity?.Name) ? "Admin" : User.Identity!.Name!;
    }
}
