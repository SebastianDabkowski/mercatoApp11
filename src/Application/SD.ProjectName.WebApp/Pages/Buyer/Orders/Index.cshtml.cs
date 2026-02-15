using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Buyer.Orders
{
    [Authorize(Policy = Permissions.BuyerPortal)]
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
        public string? SellerId { get; set; }

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        public List<OrderSummaryView> Orders { get; private set; } = new();

        public int TotalOrders { get; private set; }

        public int TotalPages { get; private set; }

        public int PageSize => DefaultPageSize;

        public bool HasFilters =>
            StatusFilters.Count > 0 || FromDate.HasValue || ToDate.HasValue || !string.IsNullOrWhiteSpace(SellerId);

        public List<string> AvailableStatuses { get; } = OrderStatuses.All.ToList();

        public List<SellerFilterOption> SellerOptions { get; private set; } = new();

        public IndexModel(OrderService orderService, UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            PageNumber = Math.Max(1, PageNumber);
            var filters = BuildFilters();
            var paged = await _orderService.GetSummariesForBuyerAsync(userId, filters, PageNumber, DefaultPageSize, HttpContext.RequestAborted);

            Orders = paged.Items;
            TotalOrders = paged.TotalCount;
            TotalPages = paged.TotalPages;
            PageNumber = paged.PageNumber;
            SellerOptions = await _orderService.GetSellerFiltersForBuyerAsync(userId, HttpContext.RequestAborted);
            return Page();
        }

        private BuyerOrderFilterOptions BuildFilters()
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

            return new BuyerOrderFilterOptions
            {
                Statuses = normalizedStatuses,
                FromDate = from,
                ToDate = to,
                SellerId = string.IsNullOrWhiteSpace(SellerId) ? null : SellerId.Trim()
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
