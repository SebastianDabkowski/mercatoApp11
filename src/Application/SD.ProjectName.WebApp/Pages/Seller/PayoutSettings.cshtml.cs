using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller;

[Authorize(Roles = AccountTypes.Seller)]
public class PayoutSettingsModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPayoutEncryptionService _payoutEncryption;

    public PayoutSettingsModel(UserManager<ApplicationUser> userManager, IPayoutEncryptionService payoutEncryption)
    {
        _userManager = userManager;
        _payoutEncryption = payoutEncryption;
    }

    [BindProperty]
    public PayoutPreferencesInput Input { get; set; } = new();

    public bool HasValidPayout { get; private set; }

    public string DefaultMethod => Input.PayoutMethod;

    public string? MaskedBankAccount { get; private set; }

    public string? MaskedRouting { get; private set; }

    public string? MaskedPayoutAccount { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        PopulateFromUser(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        PayoutValidation.Validate(ModelState, Input, nameof(Input));
        if (!ModelState.IsValid)
        {
            HasValidPayout = false;
            return Page();
        }

        Input.TrimAll();
        user.PayoutMethod = Input.PayoutMethod;
        user.PayoutSchedule = Input.PayoutSchedule;
        user.PayoutAccount = _payoutEncryption.Protect(Input.PayoutAccount);
        user.PayoutBankAccount = _payoutEncryption.Protect(Input.BankAccountNumber);
        user.PayoutBankRouting = _payoutEncryption.Protect(Input.BankRoutingNumber);
        user.PayoutUpdatedOn = DateTimeOffset.UtcNow;

        await _userManager.UpdateAsync(user);
        StatusMessage = "Payout preferences updated.";
        return RedirectToPage();
    }

    private void PopulateFromUser(ApplicationUser user)
    {
        Input = new PayoutPreferencesInput
        {
            PayoutMethod = PayoutMethods.IsValid(user.PayoutMethod) ? user.PayoutMethod : PayoutMethods.BankTransfer,
            PayoutSchedule = PayoutSchedules.IsValid(user.PayoutSchedule) ? user.PayoutSchedule : PayoutSchedules.Weekly,
            PayoutAccount = _payoutEncryption.Reveal(user.PayoutAccount),
            BankAccountNumber = _payoutEncryption.Reveal(user.PayoutBankAccount),
            BankRoutingNumber = _payoutEncryption.Reveal(user.PayoutBankRouting)
        };

        HasValidPayout = PayoutValidation.IsComplete(new PayoutPreferencesInput
        {
            PayoutMethod = Input.PayoutMethod,
            PayoutAccount = Input.PayoutAccount,
            BankAccountNumber = Input.BankAccountNumber,
            BankRoutingNumber = Input.BankRoutingNumber
        });

        MaskedBankAccount = MaskValue(Input.BankAccountNumber);
        MaskedRouting = MaskValue(Input.BankRoutingNumber, 3);
        MaskedPayoutAccount = MaskValue(Input.PayoutAccount);
    }

    private static string? MaskValue(string? value, int visible = 4)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= visible)
        {
            return new string('*', trimmed.Length);
        }

        return $"{new string('*', trimmed.Length - visible)}{trimmed[^visible..]}";
    }
}
