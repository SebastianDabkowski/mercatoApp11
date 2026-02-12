using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class KycModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IOptions<KycOptions> _kycOptions;

        public KycModel(UserManager<ApplicationUser> userManager, IOptions<KycOptions> kycOptions)
        {
            _userManager = userManager;
            _kycOptions = kycOptions;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        public string CurrentStatus { get; private set; } = KycStatuses.NotRequired;

        public DateTimeOffset? SubmittedOn { get; private set; }

        public DateTimeOffset? ApprovedOn { get; private set; }

        public bool RequireSellerKyc => _kycOptions.Value.RequireSellerKyc;

        public class InputModel
        {
            [Display(Name = "I confirm that I will provide accurate information for KYC.")]
            [Range(typeof(bool), "true", "true", ErrorMessage = "You must confirm to start the KYC flow.")]
            public bool ConfirmAccuracy { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            PopulateState(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (!ModelState.IsValid)
            {
                PopulateState(user);
                return Page();
            }

            user.KycStatus = KycStatuses.Pending;
            user.KycSubmittedOn = DateTimeOffset.UtcNow;
            await _userManager.UpdateAsync(user);

            PopulateState(user);
            StatusMessage = "KYC submission received. We will notify you when it is approved.";
            return Page();
        }

        private void PopulateState(ApplicationUser user)
        {
            CurrentStatus = user.KycStatus;
            SubmittedOn = user.KycSubmittedOn;
            ApprovedOn = user.KycApprovedOn;
        }
    }
}
