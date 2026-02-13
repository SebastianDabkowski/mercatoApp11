using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.ViewComponents
{
    public class NotificationBellViewComponent : ViewComponent
    {
        private readonly NotificationService _notificationService;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationBellViewComponent(NotificationService notificationService, UserManager<ApplicationUser> userManager)
        {
            _notificationService = notificationService;
            _userManager = userManager;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                return Content(string.Empty);
            }

            var user = await _userManager.GetUserAsync(HttpContext.User);
            if (user == null)
            {
                return Content(string.Empty);
            }

            var unreadCount = await _notificationService.GetUnreadCountAsync(user.Id, user.AccountType, HttpContext.RequestAborted);
            return View(unreadCount);
        }
    }
}
