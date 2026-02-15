using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Linq;
using System.Text.Json;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller
{
    [Authorize(Policy = Permissions.SellerWorkspace)]
    public class DashboardModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IOptions<KycOptions> _kycOptions;
        private readonly IPayoutEncryptionService _payoutEncryption;
        private readonly IOptions<SellerInternalUserOptions> _internalUserOptions;
        private readonly OrderService _orderService;
        private readonly SellerReportingService _sellerReportingService;
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public DashboardModel(
            UserManager<ApplicationUser> userManager,
            IOptions<KycOptions> kycOptions,
            IPayoutEncryptionService payoutEncryption,
            IOptions<SellerInternalUserOptions> internalUserOptions,
            OrderService orderService,
            SellerReportingService sellerReportingService)
        {
            _userManager = userManager;
            _kycOptions = kycOptions;
            _payoutEncryption = payoutEncryption;
            _internalUserOptions = internalUserOptions;
            _orderService = orderService;
            _sellerReportingService = sellerReportingService;
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

        public bool InternalUsersEnabled => _internalUserOptions.Value.Enabled;

        public bool IsStoreOwner { get; private set; }

        public string? PayoutMethod { get; private set; }

        public string? PayoutSchedule { get; private set; }

        public string? MaskedBankAccount { get; private set; }

        public string? MaskedPayoutAccount { get; private set; }

        public string? PayoutStatus { get; private set; }

        public decimal PayoutEligibleAmount { get; private set; }

        public decimal PayoutPaidAmount { get; private set; }

        public decimal PayoutThreshold { get; private set; }

        public string? PayoutErrorReference { get; private set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? SalesFromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? SalesToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SalesGranularity { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? ProductId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? CategoryId { get; set; }

        public List<SellerSalesPoint> SalesSeries { get; private set; } = new();

        public decimal SalesTotalGmv { get; private set; }

        public int SalesTotalOrders { get; private set; }

        public DateTimeOffset SalesRangeStart { get; private set; }

        public DateTimeOffset SalesRangeEnd { get; private set; }

        public string SalesSeriesJson { get; private set; } = "[]";

        public IReadOnlyList<SellerProductOption> ProductOptions { get; private set; } = Array.Empty<SellerProductOption>();

        public IReadOnlyList<SellerCategoryOption> CategoryOptions { get; private set; } = Array.Empty<SellerCategoryOption>();

        public string ActiveSalesGranularity { get; private set; } = SellerSalesGranularities.Day;

        public bool SalesHasData => SalesSeries.Any(p => p.Gmv > 0 || p.Orders > 0);

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var sellerId = user.GetSellerTenantId();
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
            var payoutView = await _orderService.GetSellerPayoutScheduleAsync(sellerId, PayoutSchedule ?? PayoutSchedules.Weekly, cancellationToken);
            PayoutStatus = payoutView.Status;
            PayoutEligibleAmount = payoutView.EligibleAmount;
            PayoutPaidAmount = payoutView.PaidAmount;
            PayoutThreshold = payoutView.Threshold;
            PayoutErrorReference = payoutView.ErrorReference;
            var (salesFrom, salesTo) = NormalizeSalesRange();
            SalesRangeStart = salesFrom;
            SalesRangeEnd = salesTo;
            ActiveSalesGranularity = SellerSalesGranularities.Normalize(SalesGranularity);
            SalesGranularity = ActiveSalesGranularity;
            var salesResult = await _sellerReportingService.GetSalesAsync(
                sellerId,
                salesFrom,
                salesTo,
                ActiveSalesGranularity,
                ProductId,
                CategoryId,
                cancellationToken);
            SalesSeries = salesResult.Series.ToList();
            SalesTotalGmv = salesResult.TotalGmv;
            SalesTotalOrders = salesResult.TotalOrders;
            ProductOptions = salesResult.ProductOptions;
            CategoryOptions = salesResult.CategoryOptions;
            ProductId = salesResult.ActiveProductId;
            CategoryId = salesResult.ActiveCategoryId;
            SalesSeriesJson = JsonSerializer.Serialize(SalesSeries, JsonOptions);
            if (InternalUsersEnabled)
            {
                IsStoreOwner = await _userManager.IsInRoleAsync(user, SellerInternalRoles.StoreOwner);
            }
            return Page();
        }

        private (DateTimeOffset From, DateTimeOffset To) NormalizeSalesRange()
        {
            var now = DateTime.UtcNow;
            var start = SalesFromDate.HasValue
                ? DateTime.SpecifyKind(SalesFromDate.Value.Date, DateTimeKind.Utc)
                : now.Date.AddDays(-29);
            var end = SalesToDate.HasValue
                ? DateTime.SpecifyKind(SalesToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : now.Date.AddDays(1).AddTicks(-1);

            if (start > end)
            {
                (start, end) = (end, start);
            }

            SalesFromDate = start.Date;
            SalesToDate = end.Date;
            return (new DateTimeOffset(start), new DateTimeOffset(end));
        }

        private void PopulatePayout(ApplicationUser user)
        {
            var payout = new PayoutPreferencesInput
            {
                PayoutMethod = PayoutMethods.IsValid(user.PayoutMethod) ? user.PayoutMethod : PayoutMethods.BankTransfer,
                PayoutSchedule = PayoutSchedules.IsValid(user.PayoutSchedule) ? user.PayoutSchedule : PayoutSchedules.Weekly,
                PayoutAccount = _payoutEncryption.Reveal(user.PayoutAccount),
                BankAccountNumber = _payoutEncryption.Reveal(user.PayoutBankAccount),
                BankRoutingNumber = _payoutEncryption.Reveal(user.PayoutBankRouting)
            };

            HasValidPayoutSettings = PayoutValidation.IsComplete(payout);
            PayoutMethod = payout.PayoutMethod;
            PayoutSchedule = payout.PayoutSchedule;
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
