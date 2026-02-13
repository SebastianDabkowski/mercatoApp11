using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Cases
{
    [Authorize(Roles = AccountTypes.Admin)]
    public class IndexModel : PageModel
    {
        private readonly OrderService _orderService;
        private const int DefaultPageSize = 20;

        [BindProperty(SupportsGet = true, Name = "status")]
        public List<string> StatusFilters { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? Query { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Type { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        public List<AdminCaseSummaryView> Cases { get; private set; } = new();

        public int TotalCases { get; private set; }

        public int TotalPages { get; private set; }

        public int PageSize => DefaultPageSize;

        public bool HasFilters =>
            StatusFilters.Count > 0
            || !string.IsNullOrWhiteSpace(Query)
            || !string.IsNullOrWhiteSpace(Type)
            || FromDate.HasValue
            || ToDate.HasValue;

        public List<string> AvailableStatuses { get; } = new()
        {
            ReturnRequestStatuses.PendingSellerReview,
            ReturnRequestStatuses.PendingBuyerInfo,
            ReturnRequestStatuses.SellerProposed,
            ReturnRequestStatuses.UnderAdminReview,
            ReturnRequestStatuses.Approved,
            ReturnRequestStatuses.Rejected,
            ReturnRequestStatuses.Completed
        };

        public List<string> AvailableTypes { get; } = new()
        {
            ReturnRequestTypes.Return,
            ReturnRequestTypes.Complaint
        };

        public IndexModel(OrderService orderService)
        {
            _orderService = orderService;
        }

        public async Task OnGetAsync()
        {
            PageNumber = Math.Max(1, PageNumber);
            var filters = BuildFilters();
            var paged = await _orderService.GetReturnCasesForAdminAsync(filters, PageNumber, DefaultPageSize, HttpContext.RequestAborted);

            Cases = paged.Items;
            TotalCases = paged.TotalCount;
            TotalPages = paged.TotalPages;
            PageNumber = paged.PageNumber;
        }

        private AdminCaseFilterOptions BuildFilters()
        {
            var normalizedStatuses = StatusFilters
                .Select(ReturnRequestStatuses.Normalize)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var from = NormalizeStartOfDay(FromDate);
            var to = NormalizeEndOfDay(ToDate);
            if (from.HasValue && to.HasValue && from > to)
            {
                (from, to) = (to, from);
            }

            return new AdminCaseFilterOptions
            {
                Statuses = normalizedStatuses,
                FromDate = from,
                ToDate = to,
                Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim(),
                Type = string.IsNullOrWhiteSpace(Type) ? null : ReturnRequestTypes.Normalize(Type)
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
