using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Buyer.Cases
{
    [Authorize(Roles = AccountTypes.Buyer)]
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

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        public List<BuyerCaseSummaryView> Cases { get; private set; } = new();

        public int TotalCases { get; private set; }

        public int TotalPages { get; private set; }

        public int PageSize => DefaultPageSize;

        public bool HasFilters => StatusFilters.Count > 0 || FromDate.HasValue || ToDate.HasValue;

        public List<string> AvailableStatuses { get; } = new()
        {
            ReturnRequestStatuses.PendingSellerReview,
            ReturnRequestStatuses.Approved,
            ReturnRequestStatuses.Rejected,
            ReturnRequestStatuses.Completed
        };

        public IndexModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var buyerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(buyerId))
            {
                return Challenge();
            }

            PageNumber = Math.Max(1, PageNumber);
            var filters = BuildFilters();
            var paged = await _orderService.GetReturnCasesForBuyerAsync(buyerId, filters, PageNumber, DefaultPageSize, HttpContext.RequestAborted);

            Cases = paged.Items;
            TotalCases = paged.TotalCount;
            TotalPages = paged.TotalPages;
            PageNumber = paged.PageNumber;

            return Page();
        }

        private ReturnCaseFilterOptions BuildFilters()
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

            return new ReturnCaseFilterOptions
            {
                Statuses = normalizedStatuses,
                FromDate = from,
                ToDate = to
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
