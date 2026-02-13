using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Users
{
    [Authorize(Roles = AccountTypes.Admin)]
    public class DetailsModel : PageModel
    {
        private readonly AdminUserService _userService;

        public AdminUserDetail? UserDetail { get; private set; }

        public DetailsModel(AdminUserService userService)
        {
            _userService = userService;
        }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            UserDetail = await _userService.GetUserAsync(id, HttpContext.RequestAborted);
            if (UserDetail == null)
            {
                return NotFound();
            }

            return Page();
        }
    }
}
