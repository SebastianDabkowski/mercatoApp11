using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Reviews
{
    [Authorize(Policy = Permissions.AdminModeration)]
    public class IndexModel : PageModel
    {
        private readonly OrderService _orderService;
        private const int DefaultPageSize = 20;

        [BindProperty(SupportsGet = true)]
        public string? Query { get; set; }

        [BindProperty(SupportsGet = true, Name = "status")]
        public List<string> Statuses { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public bool FlaggedOnly { get; set; } = true;

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        [TempData]
        public string? StatusMessage { get; set; }

        public PagedResult<ReviewModerationItem> Reviews { get; private set; } = new()
        {
            Items = new List<ReviewModerationItem>(),
            PageNumber = 1,
            PageSize = DefaultPageSize,
            TotalCount = 0
        };

        public Dictionary<string, List<ReviewAuditView>> AuditTrail { get; private set; } = new();

        public List<string> AvailableStatuses { get; } = new()
        {
            ReviewStatuses.Pending,
            ReviewStatuses.Published,
            ReviewStatuses.Hidden,
            ReviewStatuses.Rejected
        };

        public IReadOnlyList<string> RemovalReasons => ReviewModerationReasons.Allowed;

        public bool HasFilters =>
            !string.IsNullOrWhiteSpace(Query)
            || Statuses.Count > 0
            || !FlaggedOnly;

        public IndexModel(OrderService orderService)
        {
            _orderService = orderService;
        }

        public async Task OnGetAsync()
        {
            PageNumber = Math.Max(1, PageNumber);
            var filters = BuildFilters();
            Reviews = await _orderService.GetReviewsForModerationAsync(filters, PageNumber, DefaultPageSize, HttpContext.RequestAborted);
            PageNumber = Reviews.PageNumber <= 0 ? 1 : Reviews.PageNumber;
            await LoadAuditTrailAsync(Reviews.Items);
        }

        public Task<IActionResult> OnPostApproveAsync(int reviewId, string reviewType = ReviewTargetTypes.Product, string? note = null)
        {
            var normalizedType = ReviewTargetTypes.Normalize(reviewType);
            return ModerateAsync(() => normalizedType == ReviewTargetTypes.Seller
                ? _orderService.ApproveSellerRatingAsync(reviewId, GetActor(), note)
                : _orderService.ApproveReviewAsync(reviewId, GetActor(), note));
        }

        public Task<IActionResult> OnPostRejectAsync(int reviewId, string reviewType = ReviewTargetTypes.Product, string? note = null)
        {
            var normalizedType = ReviewTargetTypes.Normalize(reviewType);
            return ModerateAsync(() => normalizedType == ReviewTargetTypes.Seller
                ? _orderService.RejectSellerRatingAsync(reviewId, GetActor(), note)
                : _orderService.RejectReviewAsync(reviewId, GetActor(), note));
        }

        public Task<IActionResult> OnPostHideAsync(int reviewId, string reviewType = ReviewTargetTypes.Product, string? note = null)
        {
            var normalizedType = ReviewTargetTypes.Normalize(reviewType);
            return ModerateAsync(() => normalizedType == ReviewTargetTypes.Seller
                ? _orderService.UpdateSellerRatingVisibilityAsync(reviewId, GetActor(), false, note)
                : _orderService.UpdateReviewVisibilityAsync(reviewId, GetActor(), false, note));
        }

        public Task<IActionResult> OnPostPublishAsync(int reviewId, string reviewType = ReviewTargetTypes.Product, string? note = null)
        {
            var normalizedType = ReviewTargetTypes.Normalize(reviewType);
            return ModerateAsync(() => normalizedType == ReviewTargetTypes.Seller
                ? _orderService.UpdateSellerRatingVisibilityAsync(reviewId, GetActor(), true, note)
                : _orderService.UpdateReviewVisibilityAsync(reviewId, GetActor(), true, note));
        }

        public Task<IActionResult> OnPostFlagAsync(int reviewId, string reviewType = ReviewTargetTypes.Product, string? note = null)
        {
            var normalizedType = ReviewTargetTypes.Normalize(reviewType);
            return ModerateAsync(() => normalizedType == ReviewTargetTypes.Seller
                ? _orderService.FlagSellerRatingAsync(reviewId, GetActor(), note)
                : _orderService.FlagReviewAsync(reviewId, GetActor(), note));
        }

        private async Task<IActionResult> ModerateAsync(Func<Task<ReviewModerationResult>> action)
        {
            var result = await action();
            StatusMessage = result.Success ? "Review updated." : result.Error ?? "Unable to update review.";
            return RedirectToPage(new { Query, status = Statuses, FlaggedOnly, page = PageNumber });
        }

        private ReviewModerationFilters BuildFilters()
        {
            var normalizedStatuses = Statuses
                .Select(ReviewStatuses.Normalize)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ReviewModerationFilters
            {
                Statuses = normalizedStatuses,
                FlaggedOnly = FlaggedOnly,
                Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim()
            };
        }

        private string GetActor()
        {
            return string.IsNullOrWhiteSpace(User?.Identity?.Name) ? "Admin" : User.Identity!.Name!;
        }

        private async Task LoadAuditTrailAsync(IEnumerable<ReviewModerationItem> reviews)
        {
            AuditTrail = new Dictionary<string, List<ReviewAuditView>>();
            foreach (var review in reviews)
            {
                var audit = await _orderService.GetReviewAuditAsync(review.Id, review.Type, HttpContext.RequestAborted);
                AuditTrail[GetAuditKey(review)] = audit;
            }
        }

        public string GetAuditKey(ReviewModerationItem review)
        {
            return $"{review.Type}:{review.Id}";
        }
    }
}
