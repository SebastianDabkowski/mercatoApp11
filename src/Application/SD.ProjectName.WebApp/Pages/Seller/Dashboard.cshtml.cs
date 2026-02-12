using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller
{
    [Authorize(Roles = AccountTypes.Seller)]
    public class DashboardModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IOptions<KycOptions> _kycOptions;
        private readonly IPayoutEncryptionService _payoutEncryption;

        public DashboardModel(UserManager<ApplicationUser> userManager, IOptions<KycOptions> kycOptions, IPayoutEncryptionService payoutEncryption)
        {
            _userManager = userManager;
            _kycOptions = kycOptions;
            _payoutEncryption = payoutEncryption;
        }

        public string AccountStatus { get; private set; } = AccountStatuses.Unverified;

        public string KycStatus { get; private set; } = KycStatuses.NotRequired;

        public bool RequireSellerKyc => _kycOptions.Value.RequireSellerKyc;

        public bool NeedsKyc => RequireSellerKyc &&
                                !string.Equals(KycStatus, KycStatuses.Approved, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(KycStatus, KycStatuses.NotRequired, StringComparison.OrdinalIgnoreCase);

        public string OnboardingStatus { get; private set; } = OnboardingStatuses.NotStarted;

        public int OnboardingStep { get; private set; }

        public bool NeedsOnboarding => string.Equals(OnboardingStatus, OnboardingStatuses.NotStarted, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(OnboardingStatus, OnboardingStatuses.InProgress, StringComparison.OrdinalIgnoreCase);

        public bool OnboardingPendingReview => string.Equals(OnboardingStatus, OnboardingStatuses.PendingVerification, StringComparison.OrdinalIgnoreCase);

        public string StoreName { get; private set; } = string.Empty;

        public string StoreDescription { get; private set; } = string.Empty;

        public string ContactEmail { get; private set; } = string.Empty;

        public string? ContactPhone { get; private set; }

        public string? ContactWebsite { get; private set; }

        public string? StoreLogoPath { get; private set; }

        public bool HasStoreProfile => !string.IsNullOrEmpty(StoreName);

        public bool HasValidPayoutSettings { get; private set; }

        public string? PayoutMethod { get; private set; }

        public string? MaskedBankAccount { get; private set; }

        public string? MaskedPayoutAccount { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            AccountStatus = user.AccountStatus;
            KycStatus = user.KycStatus;
            OnboardingStatus = user.OnboardingStatus;
            OnboardingStep = user.OnboardingStep;
            StoreName = user.BusinessName ?? string.Empty;
            StoreDescription = user.StoreDescription ?? string.Empty;
            ContactEmail = string.IsNullOrWhiteSpace(user.ContactEmail) ? user.Email ?? string.Empty : user.ContactEmail;
            ContactPhone = user.ContactPhone;
            ContactWebsite = user.ContactWebsite;
            StoreLogoPath = user.StoreLogoPath;
            PopulatePayout(user);
            return Page();
        }

        private void PopulatePayout(ApplicationUser user)
        {
            var payout = new PayoutPreferencesInput
            {
                PayoutMethod = PayoutMethods.IsValid(user.PayoutMethod) ? user.PayoutMethod : PayoutMethods.BankTransfer,
                PayoutAccount = _payoutEncryption.Reveal(user.PayoutAccount),
                BankAccountNumber = _payoutEncryption.Reveal(user.PayoutBankAccount),
                BankRoutingNumber = _payoutEncryption.Reveal(user.PayoutBankRouting)
            };

            HasValidPayoutSettings = PayoutValidation.IsComplete(payout);
            PayoutMethod = payout.PayoutMethod;
            MaskedBankAccount = MaskValue(payout.BankAccountNumber);
            MaskedPayoutAccount = string.IsNullOrWhiteSpace(payout.BankAccountNumber)
                ? MaskValue(payout.PayoutAccount)
                : null;
        }

        private static string? MaskValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.Length <= 4)
            {
                return new string('*', trimmed.Length);
            }

            return $"{new string('*', trimmed.Length - 4)}{trimmed[^4..]}";
        }
    }
}
