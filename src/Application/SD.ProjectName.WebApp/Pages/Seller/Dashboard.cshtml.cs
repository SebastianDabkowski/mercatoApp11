using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class DashboardModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IOptions<KycOptions> _kycOptions;

        public DashboardModel(UserManager<ApplicationUser> userManager, IOptions<KycOptions> kycOptions)
        {
            _userManager = userManager;
            _kycOptions = kycOptions;
        }

        public string AccountStatus { get; private set; } = AccountStatuses.Unverified;

        public string KycStatus { get; private set; } = KycStatuses.NotRequired;

        public bool RequireSellerKyc => _kycOptions.Value.RequireSellerKyc;

        public bool NeedsKyc => RequireSellerKyc &&
                                !string.Equals(KycStatus, KycStatuses.Approved, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(KycStatus, KycStatuses.NotRequired, StringComparison.OrdinalIgnoreCase);

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            AccountStatus = user.AccountStatus;
            KycStatus = user.KycStatus;
            return Page();
        }
    }
}
