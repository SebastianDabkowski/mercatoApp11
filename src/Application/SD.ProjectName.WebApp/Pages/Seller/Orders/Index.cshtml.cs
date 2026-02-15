using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller.Orders
{
    [Authorize(Policy = Permissions.SellerWorkspace)]
    public class IndexModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;
        private const int DefaultPageSize = 10;

        [BindProperty(SupportsGet = true, Name = "status")]
        public List<string> StatusFilters { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Buyer { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool MissingTrackingOnly { get; set; }

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        public List<SellerOrderSummaryView> Orders { get; private set; } = new();

        public int TotalOrders { get; private set; }

        public int TotalPages { get; private set; }

        public int PageSize => DefaultPageSize;

        public bool HasFilters =>
            StatusFilters.Count > 0 || FromDate.HasValue || ToDate.HasValue || !string.IsNullOrWhiteSpace(Buyer) || MissingTrackingOnly;

        public List<string> AvailableStatuses { get; } = OrderStatuses.All.ToList();

        public IndexModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var seller = await _userManager.GetUserAsync(User);
            var sellerId = seller?.GetSellerTenantId();
            if (sellerId == null)
            {
                return Challenge();
            }

            PageNumber = Math.Max(1, PageNumber);
            var filters = BuildFilters();
            var paged = await _orderService.GetSummariesForSellerAsync(sellerId, filters, PageNumber, DefaultPageSize, HttpContext.RequestAborted);

            Orders = paged.Items;
            TotalOrders = paged.TotalCount;
            TotalPages = paged.TotalPages;
            PageNumber = paged.PageNumber;

            return Page();
        }

        public async Task<IActionResult> OnGetExportAsync()
        {
            var seller = await _userManager.GetUserAsync(User);
            var sellerId = seller?.GetSellerTenantId();
            if (sellerId == null)
            {
                return Challenge();
            }

            var filters = BuildFilters();
            var export = await _orderService.ExportSellerOrdersAsync(sellerId, filters, HttpContext.RequestAborted);
            if (export == null || export.RowCount == 0)
            {
                TempData["ErrorMessage"] = "No orders match these filters. Nothing to export.";
                return RedirectToPage(new { status = StatusFilters, FromDate, ToDate, Buyer, MissingTrackingOnly });
            }

            if (export.Truncated)
            {
                TempData["StatusMessage"] = $"Exported first {export.RowCount} of {export.TotalMatching} matching orders. Narrow filters to export all.";
            }

            var fileName = $"orders-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            return File(export.Content, "text/csv", fileName);
        }

        private SellerOrderFilterOptions BuildFilters()
        {
            var normalizedStatuses = StatusFilters
                .Select(OrderStatuses.Normalize)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var from = NormalizeStartOfDay(FromDate);
            var to = NormalizeEndOfDay(ToDate);
            if (from.HasValue && to.HasValue && from > to)
            {
                (from, to) = (to, from);
            }

            return new SellerOrderFilterOptions
            {
                Statuses = normalizedStatuses,
                FromDate = from,
                ToDate = to,
                BuyerQuery = string.IsNullOrWhiteSpace(Buyer) ? null : Buyer.Trim(),
                MissingTrackingOnly = MissingTrackingOnly
            };
        }

        private static DateTimeOffset? NormalizeStartOfDay(DateTime? date)
        {
            if (!date.HasValue)
            {
                return null;
            }

            var normalized = DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Utc);
            return new DateTimeOffset(normalized);
        }

        private static DateTimeOffset? NormalizeEndOfDay(DateTime? date)
        {
            if (!date.HasValue)
            {
                return null;
            }

            var normalized = DateTime.SpecifyKind(date.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            return new DateTimeOffset(normalized);
        }
    }
}
