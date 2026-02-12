using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Pages.Seller
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class OnboardingModel : PageModel
    {
        private const int TotalSteps = 3;
        private readonly UserManager<ApplicationUser> _userManager;

        public OnboardingModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        [BindProperty]
        public StoreProfileInput StoreProfile { get; set; } = new();

        [BindProperty]
        public VerificationInput Verification { get; set; } = new();

        [BindProperty]
        public PayoutInput Payout { get; set; } = new();

        [TempData]
        public string? StatusMessage { get; set; }

        public int Step { get; private set; } = 1;

        public int LastCompletedStep { get; private set; }

        public string OnboardingStatus { get; private set; } = OnboardingStatuses.NotStarted;

        public DateTimeOffset? CompletedOn { get; private set; }

        public bool IsSubmitted =>
            string.Equals(OnboardingStatus, OnboardingStatuses.PendingVerification, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(OnboardingStatus, OnboardingStatuses.Completed, StringComparison.OrdinalIgnoreCase);

        public async Task<IActionResult> OnGetAsync(int step = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            RefreshStateFromUser(user);
            Step = ComputeStep(step, user);
            PopulateInputs(user);
            return Page();
        }

        public async Task<IActionResult> OnPostStoreAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            Step = 1;
            RefreshStateFromUser(user);

            if (IsSubmissionComplete(user))
            {
                return RedirectToPage("Dashboard");
            }

            if (!ModelState.IsValid)
            {
                PopulateInputs(user);
                return Page();
            }

            StoreProfile.StoreName = StoreProfile.StoreName.Trim();
            StoreProfile.StoreDescription = StoreProfile.StoreDescription?.Trim() ?? string.Empty;
            var users = _userManager.Users;
            var duplicateName = users != null && users.Any(u =>
                u.Id != user.Id &&
                !string.IsNullOrEmpty(u.BusinessName) &&
                u.BusinessName.Equals(StoreProfile.StoreName, StringComparison.OrdinalIgnoreCase));

            if (duplicateName)
            {
                ModelState.AddModelError($"{nameof(StoreProfile)}.{nameof(StoreProfile.StoreName)}", "Store name is already taken. Choose another.");
                PopulateInputs(user);
                return Page();
            }

            user.BusinessName = StoreProfile.StoreName;
            user.StoreDescription = StoreProfile.StoreDescription;
            MarkInProgress(user);
            if (user.OnboardingStep < 1)
            {
                user.OnboardingStep = 1;
            }

            await _userManager.UpdateAsync(user);
            return RedirectToPage(new { step = 2 });
        }

        public async Task<IActionResult> OnPostVerificationAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            Step = 2;
            RefreshStateFromUser(user);

            if (IsSubmissionComplete(user))
            {
                return RedirectToPage("Dashboard");
            }

            if (!ModelState.IsValid)
            {
                PopulateInputs(user);
                return Page();
            }

            user.TaxId = Verification.TaxId;
            user.Address = Verification.Address;
            user.Country = Verification.Country;
            MarkInProgress(user);
            if (user.OnboardingStep < 2)
            {
                user.OnboardingStep = 2;
            }

            await _userManager.UpdateAsync(user);
            return RedirectToPage(new { step = 3 });
        }

        public async Task<IActionResult> OnPostPayoutAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            Step = TotalSteps;
            RefreshStateFromUser(user);

            if (IsSubmissionComplete(user))
            {
                return RedirectToPage("Dashboard");
            }

            if (!ModelState.IsValid)
            {
                PopulateInputs(user);
                return Page();
            }

            user.PayoutMethod = Payout.PayoutMethod;
            user.PayoutAccount = Payout.PayoutAccount;
            MarkInProgress(user);
            user.OnboardingStep = TotalSteps;
            user.OnboardingStatus = OnboardingStatuses.PendingVerification;
            user.OnboardingCompletedOn = DateTimeOffset.UtcNow;

            await _userManager.UpdateAsync(user);
            StatusMessage = "Store profile submitted and pending verification.";
            return RedirectToPage("Dashboard");
        }

        private void RefreshStateFromUser(ApplicationUser user)
        {
            OnboardingStatus = user.OnboardingStatus;
            LastCompletedStep = NormalizeCompletedStep(user.OnboardingStep);
            CompletedOn = user.OnboardingCompletedOn;
        }

        private static bool IsSubmissionComplete(ApplicationUser user)
        {
            return string.Equals(user.OnboardingStatus, OnboardingStatuses.PendingVerification, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(user.OnboardingStatus, OnboardingStatuses.Completed, StringComparison.OrdinalIgnoreCase);
        }

        private int ComputeStep(int requestedStep, ApplicationUser user)
        {
            if (IsSubmissionComplete(user))
            {
                return TotalSteps;
            }

            var nextStep = NormalizeCompletedStep(user.OnboardingStep) + 1;
            if (nextStep > TotalSteps)
            {
                nextStep = TotalSteps;
            }

            if (requestedStep < nextStep)
            {
                return nextStep;
            }

            return requestedStep > TotalSteps ? TotalSteps : requestedStep;
        }

        private int NormalizeCompletedStep(int step)
        {
            if (step < 0)
            {
                return 0;
            }

            return step > TotalSteps ? TotalSteps : step;
        }

        private void PopulateInputs(ApplicationUser user)
        {
            StoreProfile ??= new StoreProfileInput();
            StoreProfile.StoreName = user.BusinessName ?? string.Empty;
            StoreProfile.StoreDescription = user.StoreDescription ?? string.Empty;

            Verification ??= new VerificationInput();
            Verification.TaxId = user.TaxId ?? string.Empty;
            Verification.Address = user.Address ?? string.Empty;
            Verification.Country = user.Country ?? string.Empty;

            Payout ??= new PayoutInput();
            Payout.PayoutMethod = string.IsNullOrWhiteSpace(user.PayoutMethod) ? "BankTransfer" : user.PayoutMethod;
            Payout.PayoutAccount = user.PayoutAccount ?? string.Empty;
        }

        private static void MarkInProgress(ApplicationUser user)
        {
            if (user.OnboardingStartedOn == null)
            {
                user.OnboardingStartedOn = DateTimeOffset.UtcNow;
            }

            user.OnboardingStatus = OnboardingStatuses.InProgress;
        }

        public class StoreProfileInput
        {
            [Required]
            [Display(Name = "Store name")]
            [StringLength(256)]
            public string StoreName { get; set; } = string.Empty;

            [Required]
            [Display(Name = "Store description")]
            [StringLength(2048)]
            public string StoreDescription { get; set; } = string.Empty;
        }

        public class VerificationInput
        {
            [Required]
            [Display(Name = "Tax ID")]
            [StringLength(128)]
            public string TaxId { get; set; } = string.Empty;

            [Required]
            [Display(Name = "Business address")]
            [StringLength(512)]
            public string Address { get; set; } = string.Empty;

            [Required]
            [StringLength(120)]
            public string Country { get; set; } = string.Empty;
        }

        public class PayoutInput
        {
            [Required]
            [Display(Name = "Payout method")]
            [StringLength(64)]
            public string PayoutMethod { get; set; } = "BankTransfer";

            [Required]
            [Display(Name = "Payout account or email")]
            [StringLength(256)]
            public string PayoutAccount { get; set; } = string.Empty;
        }
    }
}
