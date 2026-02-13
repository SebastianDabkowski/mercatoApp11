using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Notifications
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly NotificationService _notificationService;
        private readonly UserManager<ApplicationUser> _userManager;
        private const int DefaultPageSize = 8;
        private readonly PushNotificationOptions _pushOptions;
        private readonly PushSubscriptionStore _subscriptionStore;

        public PagedResult<NotificationItem> Notifications { get; private set; } = new()
        {
            Items = new List<NotificationItem>(),
            PageNumber = 1,
            PageSize = DefaultPageSize,
            TotalCount = 0
        };

        public int UnreadCount { get; private set; }

        public bool PushNotificationsEnabled { get; private set; }

        public bool HasPushSubscription { get; private set; }

        public string PushPublicKey { get; private set; } = string.Empty;

        [BindProperty(SupportsGet = true, Name = "filter")]
        public string Filter { get; set; } = NotificationFilterOptions.Unread;

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        public IndexModel(
            NotificationService notificationService,
            UserManager<ApplicationUser> userManager,
            PushNotificationOptions pushOptions,
            PushSubscriptionStore subscriptionStore)
        {
            _notificationService = notificationService;
            _userManager = userManager;
            _pushOptions = pushOptions;
            _subscriptionStore = subscriptionStore;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            Filter = NotificationFilterOptions.Normalize(Filter);
            PageNumber = Math.Max(1, PageNumber);
            var result = await _notificationService.GetFeedAsync(
                user.Id,
                user.AccountType,
                NotificationFilterOptions.ToFilter(Filter),
                PageNumber,
                DefaultPageSize,
                HttpContext.RequestAborted);

            Notifications = result.Items;
            UnreadCount = result.UnreadCount;
            PushNotificationsEnabled = _pushOptions.Enabled;
            PushPublicKey = _pushOptions.PublicKey ?? string.Empty;
            HasPushSubscription = _subscriptionStore.HasAny(user.Id);
            return Page();
        }

        public async Task<IActionResult> OnPostMarkAsReadAsync(Guid notificationId, string? filter, int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var normalizedFilter = NotificationFilterOptions.Normalize(filter);
            var pageNumber = Math.Max(1, page);

            await _notificationService.MarkAsReadAsync(user.Id, notificationId, HttpContext.RequestAborted);
            return RedirectToPage(new { filter = normalizedFilter, page = pageNumber });
        }
    }
}
