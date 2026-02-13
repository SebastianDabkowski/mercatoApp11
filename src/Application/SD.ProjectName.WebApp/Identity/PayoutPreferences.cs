using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SD.ProjectName.WebApp.Identity;

public static class PayoutMethods
{
    public const string BankTransfer = "BankTransfer";
    public const string Paypal = "Paypal";
    public const string ManualReview = "ManualReview";

    public static readonly string[] Allowed = [BankTransfer, Paypal, ManualReview];

    public static bool IsValid(string? method) =>
        !string.IsNullOrWhiteSpace(method) &&
        Allowed.Contains(method, StringComparer.OrdinalIgnoreCase);
}

public static class PayoutSchedules
{
    public const string Weekly = "Weekly";
    public const string Manual = "Manual";

    public static readonly string[] Allowed = [Weekly, Manual];

    public static bool IsValid(string? schedule) =>
        !string.IsNullOrWhiteSpace(schedule) &&
        Allowed.Contains(schedule, StringComparer.OrdinalIgnoreCase);
}

public class PayoutPreferencesInput
{
    [Required]
    [Display(Name = "Default payout method")]
    [StringLength(64)]
    public string PayoutMethod { get; set; } = PayoutMethods.BankTransfer;

    [Required]
    [Display(Name = "Payout schedule")]
    [StringLength(32)]
    public string PayoutSchedule { get; set; } = PayoutSchedules.Weekly;

    [Display(Name = "Payout account or email")]
    [StringLength(256)]
    public string? PayoutAccount { get; set; }

    [Display(Name = "Bank account or IBAN")]
    [StringLength(256)]
    public string? BankAccountNumber { get; set; }

    [Display(Name = "Routing number / sort code")]
    [StringLength(128)]
    public string? BankRoutingNumber { get; set; }

    public void TrimAll()
    {
        PayoutMethod = PayoutMethod?.Trim() ?? string.Empty;
        PayoutSchedule = PayoutSchedule?.Trim() ?? string.Empty;
        PayoutAccount = string.IsNullOrWhiteSpace(PayoutAccount) ? null : PayoutAccount.Trim();
        BankAccountNumber = string.IsNullOrWhiteSpace(BankAccountNumber) ? null : BankAccountNumber.Replace(" ", string.Empty).Trim();
        BankRoutingNumber = string.IsNullOrWhiteSpace(BankRoutingNumber) ? null : BankRoutingNumber.Trim();
    }
}

public static class PayoutValidation
{
    private static readonly Regex BankAccountPattern = new(@"^[A-Za-z0-9]{8,34}$", RegexOptions.Compiled);
    private static readonly Regex RoutingPattern = new(@"^[A-Za-z0-9\-]{4,18}$", RegexOptions.Compiled);
    private static readonly EmailAddressAttribute EmailValidator = new();

    public static void Validate(ModelStateDictionary modelState, PayoutPreferencesInput input, string fieldPrefix)
    {
        input.TrimAll();
        var prefix = string.IsNullOrWhiteSpace(fieldPrefix) ? string.Empty : $"{fieldPrefix}.";

        if (!PayoutSchedules.IsValid(input.PayoutSchedule))
        {
            modelState.AddModelError($"{prefix}{nameof(PayoutPreferencesInput.PayoutSchedule)}", "Select a supported payout schedule.");
        }

        if (!PayoutMethods.IsValid(input.PayoutMethod))
        {
            modelState.AddModelError($"{prefix}{nameof(PayoutPreferencesInput.PayoutMethod)}", "Select a supported payout method.");
            return;
        }

        if (string.Equals(input.PayoutMethod, PayoutMethods.BankTransfer, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(input.BankAccountNumber))
            {
                modelState.AddModelError($"{prefix}{nameof(PayoutPreferencesInput.BankAccountNumber)}", "Bank account or IBAN is required for bank transfers.");
            }
            else if (!BankAccountPattern.IsMatch(input.BankAccountNumber))
            {
                modelState.AddModelError($"{prefix}{nameof(PayoutPreferencesInput.BankAccountNumber)}", "Enter a valid bank account or IBAN (8-34 letters or digits).");
            }

            if (string.IsNullOrWhiteSpace(input.BankRoutingNumber))
            {
                modelState.AddModelError($"{prefix}{nameof(PayoutPreferencesInput.BankRoutingNumber)}", "Routing number or sort code is required for bank transfers.");
            }
            else if (!RoutingPattern.IsMatch(input.BankRoutingNumber))
            {
                modelState.AddModelError($"{prefix}{nameof(PayoutPreferencesInput.BankRoutingNumber)}", "Enter a valid routing number or sort code.");
            }
        }
        else if (string.Equals(input.PayoutMethod, PayoutMethods.Paypal, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(input.PayoutAccount))
            {
                modelState.AddModelError($"{prefix}{nameof(PayoutPreferencesInput.PayoutAccount)}", "PayPal payouts require an account email.");
            }
            else if (!EmailValidator.IsValid(input.PayoutAccount))
            {
                modelState.AddModelError($"{prefix}{nameof(PayoutPreferencesInput.PayoutAccount)}", "Enter a valid payout email.");
            }
        }
    }

    public static bool IsComplete(PayoutPreferencesInput input)
    {
        input.TrimAll();

        if (!PayoutMethods.IsValid(input.PayoutMethod))
        {
            return false;
        }

        if (!PayoutSchedules.IsValid(input.PayoutSchedule))
        {
            return false;
        }

        if (string.Equals(input.PayoutMethod, PayoutMethods.BankTransfer, StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(input.BankAccountNumber) &&
                   !string.IsNullOrWhiteSpace(input.BankRoutingNumber) &&
                   BankAccountPattern.IsMatch(input.BankAccountNumber) &&
                   RoutingPattern.IsMatch(input.BankRoutingNumber);
        }

        if (string.Equals(input.PayoutMethod, PayoutMethods.Paypal, StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(input.PayoutAccount) &&
                   EmailValidator.IsValid(input.PayoutAccount);
        }

        return true;
    }
}
