using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Reports
{
    [Authorize(Policy = Permissions.AdminReports)]
    public class OrdersModel : PageModel
    {
        private readonly AdminReportingService _reportingService;
        private readonly AdminReportOptions _options;
        private const int MinPageSize = 10;
        private const int MaxPageSize = 200;

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SellerId { get; set; }

        [BindProperty(SupportsGet = true, Name = "status")]
        public List<string> StatusFilters { get; set; } = new();

        [BindProperty(SupportsGet = true, Name = "paymentStatus")]
        public List<string> PaymentStatusFilters { get; set; } = new();

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        public IReadOnlyList<AdminOrderReportRow> Rows { get; private set; } = Array.Empty<AdminOrderReportRow>();

        public int TotalRows { get; private set; }

        public int TotalPages { get; private set; }

        public int PageSize { get; private set; }

        public decimal TotalOrderValue { get; private set; }

        public decimal TotalCommission { get; private set; }

        public decimal TotalPayout { get; private set; }

        public List<string> AvailableStatuses { get; } = OrderStatuses.All.ToList();

        public List<string> AvailablePaymentStatuses { get; } = PaymentStatuses.All.ToList();

        public bool HasFilters =>
            StatusFilters.Count > 0 || PaymentStatusFilters.Count > 0 || !string.IsNullOrWhiteSpace(SellerId) || FromDate.HasValue || ToDate.HasValue;

        public OrdersModel(AdminReportingService reportingService, AdminReportOptions options)
        {
            _reportingService = reportingService;
            _options = options;
        }

        public async Task OnGetAsync()
        {
            if (!FromDate.HasValue && !ToDate.HasValue)
            {
                var today = DateTime.UtcNow.Date;
                FromDate = today.AddDays(-30);
                ToDate = today;
            }

            PageSize = ResolvePageSize();
            PageNumber = Math.Max(1, PageNumber);

            var filters = BuildFilters();
            var result = await _reportingService.GetOrderReportAsync(filters, PageNumber, PageSize, HttpContext.RequestAborted);

            Rows = result.Rows;
            TotalRows = result.TotalCount;
            TotalPages = result.TotalPages;
            PageNumber = result.PageNumber;
            TotalOrderValue = result.TotalOrderValue;
            TotalCommission = result.TotalCommission;
            TotalPayout = result.TotalPayout;

            StatusFilters = filters.Statuses;
            PaymentStatusFilters = filters.PaymentStatuses;
            FromDate = filters.FromDate?.UtcDateTime.Date;
            ToDate = filters.ToDate?.UtcDateTime.Date;
        }

        public async Task<IActionResult> OnGetExportAsync()
        {
            var filters = BuildFilters();
            var export = await _reportingService.ExportOrdersAsync(filters, HttpContext.RequestAborted);
            if (export == null || export.RowCount == 0)
            {
                TempData["ErrorMessage"] = "No orders match these filters. Nothing to export.";
                return RedirectToPage(new
                {
                    status = StatusFilters,
                    paymentStatus = PaymentStatusFilters,
                    FromDate,
                    ToDate,
                    SellerId
                });
            }

            if (export.Truncated)
            {
                TempData["StatusMessage"] = $"Exported first {export.RowCount} of {export.TotalMatching} matching rows. Narrow filters to export all.";
            }

            var fileName = $"admin-order-report-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            return File(export.Content, "text/csv", fileName);
        }

        private AdminOrderReportFilterOptions BuildFilters()
        {
            var normalizedStatuses = StatusFilters
                .Select(OrderStatuses.Normalize)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var normalizedPaymentStatuses = PaymentStatusFilters
                .Select(PaymentStatuses.Normalize)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var from = NormalizeStartOfDay(FromDate);
            var to = NormalizeEndOfDay(ToDate);
            if (from.HasValue && to.HasValue && from > to)
            {
                (from, to) = (to, from);
            }

            return new AdminOrderReportFilterOptions
            {
                FromDate = from,
                ToDate = to,
                SellerId = string.IsNullOrWhiteSpace(SellerId) ? null : SellerId.Trim(),
                Statuses = normalizedStatuses,
                PaymentStatuses = normalizedPaymentStatuses
            };
        }

        private int ResolvePageSize()
        {
            var fallback = _options.PreviewPageSize <= 0 ? 50 : _options.PreviewPageSize;
            return Math.Clamp(fallback, MinPageSize, MaxPageSize);
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
