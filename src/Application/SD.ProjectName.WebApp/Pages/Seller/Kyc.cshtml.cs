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

        public class InputModel : IValidatableObject
        {
            [Required]
            [Display(Name = "Seller type")]
            public string SellerType { get; set; } = SellerTypes.Individual;

            [Display(Name = "Company name")]
            [StringLength(256)]
            public string CompanyName { get; set; } = string.Empty;

            [Display(Name = "Registration number")]
            [StringLength(128)]
            public string RegistrationNumber { get; set; } = string.Empty;

            [Display(Name = "Tax ID")]
            [StringLength(128)]
            public string TaxId { get; set; } = string.Empty;

            [Display(Name = "Registered address")]
            [StringLength(512)]
            public string RegisteredAddress { get; set; } = string.Empty;

            [Display(Name = "Contact person")]
            [StringLength(256)]
            public string ContactPerson { get; set; } = string.Empty;

            [Display(Name = "Full name")]
            [StringLength(256)]
            public string FullName { get; set; } = string.Empty;

            [Display(Name = "Personal ID number")]
            [StringLength(128)]
            public string PersonalIdNumber { get; set; } = string.Empty;

            [Display(Name = "Address")]
            [StringLength(512)]
            public string Address { get; set; } = string.Empty;

            [Display(Name = "Contact email")]
            [EmailAddress]
            [StringLength(256)]
            public string ContactEmail { get; set; } = string.Empty;

            [Display(Name = "Contact phone")]
            [StringLength(64)]
            public string? ContactPhone { get; set; } = string.Empty;

            [Display(Name = "I confirm that I will provide accurate information for KYC.")]
            [Range(typeof(bool), "true", "true", ErrorMessage = "You must confirm to start the KYC flow.")]
            public bool ConfirmAccuracy { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                var normalizedType = SellerTypes.Allowed.FirstOrDefault(t => t.Equals(SellerType, StringComparison.OrdinalIgnoreCase));
                if (normalizedType == null)
                {
                    yield return new ValidationResult("Select a seller type.", [nameof(SellerType)]);
                    yield break;
                }

                if (string.Equals(normalizedType, SellerTypes.Company, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(CompanyName))
                    {
                        yield return new ValidationResult("Company name is required for company sellers.", [nameof(CompanyName)]);
                    }

                    if (string.IsNullOrWhiteSpace(RegistrationNumber))
                    {
                        yield return new ValidationResult("Registration number is required for company sellers.", [nameof(RegistrationNumber)]);
                    }

                    if (string.IsNullOrWhiteSpace(TaxId))
                    {
                        yield return new ValidationResult("Tax ID is required for company sellers.", [nameof(TaxId)]);
                    }

                    if (string.IsNullOrWhiteSpace(RegisteredAddress))
                    {
                        yield return new ValidationResult("Registered address is required for company sellers.", [nameof(RegisteredAddress)]);
                    }

                    if (string.IsNullOrWhiteSpace(ContactPerson))
                    {
                        yield return new ValidationResult("Contact person is required for company sellers.", [nameof(ContactPerson)]);
                    }

                    if (string.IsNullOrWhiteSpace(ContactEmail))
                    {
                        yield return new ValidationResult("Contact email is required for company sellers.", [nameof(ContactEmail)]);
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(FullName))
                    {
                        yield return new ValidationResult("Full name is required for individual sellers.", [nameof(FullName)]);
                    }

                    if (string.IsNullOrWhiteSpace(PersonalIdNumber))
                    {
                        yield return new ValidationResult("Personal ID number is required for individual sellers.", [nameof(PersonalIdNumber)]);
                    }

                    if (string.IsNullOrWhiteSpace(Address))
                    {
                        yield return new ValidationResult("Address is required for individual sellers.", [nameof(Address)]);
                    }

                    if (string.IsNullOrWhiteSpace(ContactEmail))
                    {
                        yield return new ValidationResult("Contact email is required for individual sellers.", [nameof(ContactEmail)]);
                    }
                }
            }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            PopulateState(user);
            PopulateInput(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (string.Equals(user.KycStatus, KycStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                PopulateState(user);
                PopulateInput(user);
                ModelState.AddModelError(string.Empty, "Verification is already pending review. We will notify you when it is completed.");
                return Page();
            }

            if (!ModelState.IsValid)
            {
                PopulateState(user);
                return Page();
            }

            var sellerType = NormalizeSellerType(Input.SellerType);
            user.SellerType = sellerType;
            user.ContactEmail = (Input.ContactEmail ?? string.Empty).Trim();
            user.ContactPhone = string.IsNullOrWhiteSpace(Input.ContactPhone) ? null : Input.ContactPhone.Trim();

            if (string.Equals(sellerType, SellerTypes.Company, StringComparison.OrdinalIgnoreCase))
            {
                user.BusinessName = Input.CompanyName.Trim();
                user.CompanyRegistrationNumber = Input.RegistrationNumber.Trim();
                user.TaxId = Input.TaxId.Trim();
                user.Address = Input.RegisteredAddress.Trim();
                user.VerificationContactName = Input.ContactPerson.Trim();
                if (string.IsNullOrWhiteSpace(user.FullName))
                {
                    user.FullName = Input.ContactPerson.Trim();
                }
                user.PersonalIdNumber = null;
            }
            else
            {
                user.FullName = Input.FullName.Trim();
                user.PersonalIdNumber = Input.PersonalIdNumber.Trim();
                user.Address = Input.Address.Trim();
                user.VerificationContactName = null;
                user.CompanyRegistrationNumber = null;
            }

            user.KycStatus = KycStatuses.Pending;
            user.KycSubmittedOn = DateTimeOffset.UtcNow;
            await _userManager.UpdateAsync(user);

            PopulateState(user);
            PopulateInput(user);
            StatusMessage = "Verification submitted and pending review. We will notify you when it's approved.";
            return Page();
        }

        private void PopulateState(ApplicationUser user)
        {
            CurrentStatus = user.KycStatus;
            SubmittedOn = user.KycSubmittedOn;
            ApprovedOn = user.KycApprovedOn;
        }

        private void PopulateInput(ApplicationUser user)
        {
            Input ??= new InputModel();
            var sellerType = NormalizeSellerType(user.SellerType);
            Input.SellerType = sellerType;

            Input.ContactEmail = string.IsNullOrWhiteSpace(user.ContactEmail) ? user.Email ?? string.Empty : user.ContactEmail;
            Input.ContactPhone = user.ContactPhone ?? string.Empty;

            if (string.Equals(sellerType, SellerTypes.Company, StringComparison.OrdinalIgnoreCase))
            {
                Input.CompanyName = user.BusinessName ?? string.Empty;
                Input.RegistrationNumber = user.CompanyRegistrationNumber ?? string.Empty;
                Input.TaxId = user.TaxId ?? string.Empty;
                Input.RegisteredAddress = user.Address ?? string.Empty;
                Input.ContactPerson = user.VerificationContactName ?? (string.IsNullOrWhiteSpace(user.FullName) ? string.Empty : user.FullName);
                Input.FullName = string.Empty;
                Input.PersonalIdNumber = string.Empty;
                Input.Address = string.Empty;
            }
            else
            {
                Input.CompanyName = string.Empty;
                Input.RegistrationNumber = string.Empty;
                Input.TaxId = user.TaxId ?? string.Empty;
                Input.RegisteredAddress = string.Empty;
                Input.ContactPerson = string.Empty;
                Input.FullName = string.IsNullOrWhiteSpace(user.FullName) ? string.Empty : user.FullName;
                Input.PersonalIdNumber = user.PersonalIdNumber ?? string.Empty;
                Input.Address = user.Address ?? string.Empty;
            }
        }

        private static string NormalizeSellerType(string? sellerType)
        {
            return SellerTypes.Allowed.FirstOrDefault(t => t.Equals(sellerType, StringComparison.OrdinalIgnoreCase)) ?? SellerTypes.Individual;
        }
    }
}
