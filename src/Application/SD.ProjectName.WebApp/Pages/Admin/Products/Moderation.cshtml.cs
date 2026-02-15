using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Products
{
    [Authorize(Policy = Permissions.AdminModeration)]
    public class ModerationModel : PageModel
    {
        private const int DefaultPageSize = 20;
        private readonly ProductModerationService _moderationService;
        private readonly ManageCategories _manageCategories;

        public ModerationModel(ProductModerationService moderationService, ManageCategories manageCategories)
        {
            _moderationService = moderationService;
            _manageCategories = manageCategories;
        }

        [BindProperty(SupportsGet = true, Name = "status")]
        public List<string> Statuses { get; set; } = new() { ProductModerationStatuses.Pending };

        [BindProperty(SupportsGet = true, Name = "category")]
        public int? CategoryId { get; set; }

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        [BindProperty]
        public List<int> SelectedIds { get; set; } = new();

        [BindProperty]
        [Display(Name = "Moderator note")]
        public string? ModerationNote { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public PagedResult<ProductModerationItem> Products { get; private set; } = new()
        {
            Items = new List<ProductModerationItem>(),
            PageNumber = 1,
            PageSize = DefaultPageSize,
            TotalCount = 0
        };

        public Dictionary<int, List<ProductModerationAuditView>> AuditTrail { get; private set; } = new();

        public List<SelectListItem> CategoryOptions { get; private set; } = new();

        public List<string> AvailableStatuses { get; } = new()
        {
            ProductModerationStatuses.Pending,
            ProductModerationStatuses.Approved,
            ProductModerationStatuses.Rejected
        };

        public bool HasFilters => (Statuses?.Count ?? 0) > 0 || CategoryId.HasValue;

        public async Task OnGetAsync()
        {
            await LoadAsync();
        }

        public async Task<IActionResult> OnPostApproveAsync()
        {
            var actor = GetActor();
            var result = await _moderationService.ApproveAsync(SelectedIds, actor, ModerationNote);
            StatusMessage = result.Success ? "Products approved and published." : (result.Error ?? "Unable to approve products.");

            return RedirectToPage(new { status = Statuses, category = CategoryId, page = PageNumber });
        }

        public async Task<IActionResult> OnPostRejectAsync()
        {
            if (string.IsNullOrWhiteSpace(ModerationNote))
            {
                StatusMessage = "Provide a rejection reason.";
                return RedirectToPage(new { status = Statuses, category = CategoryId, page = PageNumber });
            }

            var actor = GetActor();
            var result = await _moderationService.RejectAsync(SelectedIds, actor, ModerationNote);
            StatusMessage = result.Success ? "Products rejected." : (result.Error ?? "Unable to reject products.");

            return RedirectToPage(new { status = Statuses, category = CategoryId, page = PageNumber });
        }

        private async Task LoadAsync()
        {
            var filters = new ProductModerationFilters
            {
                Statuses = Statuses.Select(ProductModerationStatuses.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                CategoryId = CategoryId
            };

            PageNumber = Math.Max(1, PageNumber);
            Products = await _moderationService.GetQueueAsync(filters, PageNumber, DefaultPageSize, HttpContext.RequestAborted);
            PageNumber = Products.PageNumber <= 0 ? 1 : Products.PageNumber;

            await LoadAuditTrailAsync(Products.Items);
            await LoadCategoriesAsync();
        }

        private async Task LoadAuditTrailAsync(IEnumerable<ProductModerationItem> items)
        {
            AuditTrail = new Dictionary<int, List<ProductModerationAuditView>>();
            foreach (var item in items)
            {
                var audit = await _moderationService.GetAuditAsync(item.Id, HttpContext.RequestAborted);
                AuditTrail[item.Id] = audit;
            }
        }

        private async Task LoadCategoriesAsync()
        {
            var tree = await _manageCategories.GetTree();
            CategoryOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = string.Empty, Text = "All categories", Selected = !CategoryId.HasValue }
            };

            CategoryOptions.AddRange(tree.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.FullPath,
                Selected = CategoryId.HasValue && CategoryId.Value == c.Id
            }));
        }

        private string GetActor() => string.IsNullOrWhiteSpace(User?.Identity?.Name) ? "Admin" : User.Identity!.Name!;
    }
}
