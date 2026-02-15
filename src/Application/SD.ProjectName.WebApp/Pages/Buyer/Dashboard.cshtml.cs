using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Buyer
{
    [Authorize(Policy = Permissions.BuyerPortal)]
    public class DashboardModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
