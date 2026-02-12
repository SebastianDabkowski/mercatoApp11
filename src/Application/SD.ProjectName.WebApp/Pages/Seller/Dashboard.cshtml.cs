using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class DashboardModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
