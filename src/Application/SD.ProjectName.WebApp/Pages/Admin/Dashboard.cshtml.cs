using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Admin
{
    [Authorize(Roles = AccountTypes.Admin)]
    public class DashboardModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
