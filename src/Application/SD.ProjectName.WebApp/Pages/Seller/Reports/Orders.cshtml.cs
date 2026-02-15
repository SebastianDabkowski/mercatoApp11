using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller.Reports
{
    [Authorize(Policy = Permissions.SellerWorkspace)]
    public class OrdersModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;
        private const int DefaultPageSize = 50;

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true, Name = "status")]
        public List<string> StatusFilters { get; set; } = new();

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        public IReadOnlyList<SellerOrderReportRow> Rows { get; private set; } = Array.Empty<SellerOrderReportRow>();

        public int TotalRows { get; private set; }

        public int TotalPages { get; private set; }

        public decimal TotalOrderValue { get; private set; }

        public decimal TotalCommission { get; private set; }

        public decimal TotalNet { get; private set; }

        public int PageSize => DefaultPageSize;

        public List<string> AvailableStatuses { get; } = OrderStatuses.All.ToList();

        public bool HasFilters => StatusFilters.Count > 0 || FromDate.HasValue || ToDate.HasValue;

        public OrdersModel(OrderService orderService, UserManager<ApplicationUser> userManager)
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

            if (!FromDate.HasValue && !ToDate.HasValue)
            {
                var today = DateTime.UtcNow.Date;
                FromDate = today.AddDays(-30);
                ToDate = today;
            }

            PageNumber = Math.Max(1, PageNumber);
            var filters = BuildFilters();
            var result = await _orderService.GetSellerOrderReportAsync(sellerId, filters, PageNumber, DefaultPageSize, HttpContext.RequestAborted);

            Rows = result.Rows;
            TotalRows = result.TotalCount;
            TotalPages = result.TotalPages;
            PageNumber = result.PageNumber;
            TotalOrderValue = result.TotalOrderValue;
            TotalCommission = result.TotalCommission;
            TotalNet = result.TotalNet;

            StatusFilters = filters.Statuses;
            FromDate = filters.FromDate?.UtcDateTime.Date;
            ToDate = filters.ToDate?.UtcDateTime.Date;

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
            var export = await _orderService.ExportSellerOrderReportAsync(sellerId, filters, HttpContext.RequestAborted);
            if (export == null || export.RowCount == 0)
            {
                TempData["ErrorMessage"] = "No orders match these filters. Nothing to export.";
                return RedirectToPage(new { status = StatusFilters, FromDate, ToDate });
            }

            if (export.Truncated)
            {
                TempData["StatusMessage"] = $"Exported first {export.RowCount} of {export.TotalMatching} matching rows. Narrow filters to export all.";
            }

            var fileName = $"seller-order-report-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            return File(export.Content, "text/csv", fileName);
        }

        private SellerOrderReportFilterOptions BuildFilters()
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

            return new SellerOrderReportFilterOptions
            {
                FromDate = from,
                ToDate = to,
                Statuses = normalizedStatuses
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
