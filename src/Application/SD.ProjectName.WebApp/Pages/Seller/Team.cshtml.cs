using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller
{
    [Authorize(Policy = Permissions.SellerWorkspace)]
    public class TeamModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IOptions<SellerInternalUserOptions> _featureOptions;
        private readonly ILogger<TeamModel> _logger;

        public TeamModel(
            ApplicationDbContext dbContext,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            IOptions<SellerInternalUserOptions> featureOptions,
            ILogger<TeamModel> logger)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _emailSender = emailSender;
            _featureOptions = featureOptions;
            _logger = logger;
        }

        public bool FeatureEnabled => _featureOptions.Value.Enabled;

        public IList<SellerTeamMember> Members { get; private set; } = new List<SellerTeamMember>();

        public string? AlertMessage { get; private set; }

        [BindProperty]
        public InviteInput Invite { get; set; } = new();

        public class InviteInput
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required]
            public string Role { get; set; } = SellerInternalRoles.CatalogManager;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!FeatureEnabled)
            {
                return NotFound();
            }

            var owner = await GetCurrentOwnerAsync();
            if (owner == null)
            {
                return Forbid();
            }

            Members = await LoadMembersAsync(owner.StoreOwnerId ?? owner.Id);
            AlertMessage = TempData["TeamAlert"] as string;
            return Page();
        }

        public async Task<IActionResult> OnPostInviteAsync()
        {
            if (!FeatureEnabled)
            {
                return NotFound();
            }

            var owner = await GetCurrentOwnerAsync();
            if (owner == null)
            {
                return Forbid();
            }

            if (!SellerInternalRoles.IsValid(Invite.Role))
            {
                ModelState.AddModelError(nameof(Invite.Role), "Choose a valid role.");
            }

            if (ModelState.IsValid)
            {
                var normalizedEmail = Invite.Email.Trim();
                var ownerId = owner.StoreOwnerId ?? owner.Id;
                var alreadyPending = await _dbContext.SellerTeamMembers
                    .AnyAsync(m => m.StoreOwnerId == ownerId &&
                                   m.Email == normalizedEmail &&
                                   m.Status != SellerInternalUserStatuses.Deactivated);
                if (alreadyPending)
                {
                    ModelState.AddModelError(nameof(Invite.Email), "An invitation or active user already exists for this email.");
                }
                else
                {
                    var member = new SellerTeamMember
                    {
                        StoreOwnerId = ownerId,
                        Email = normalizedEmail,
                        Role = Invite.Role,
                        Status = SellerInternalUserStatuses.Pending,
                        InvitationCode = Guid.NewGuid().ToString("N"),
                        InvitedOn = DateTimeOffset.UtcNow
                    };

                    _dbContext.SellerTeamMembers.Add(member);
                    await _dbContext.SaveChangesAsync();

                    await SendInvitationEmailAsync(member);
                    TempData["TeamAlert"] = $"Invitation sent to {member.Email} with role {member.Role}.";
                    return RedirectToPage();
                }
            }

            Members = await LoadMembersAsync(owner.StoreOwnerId ?? owner.Id);
            return Page();
        }

        public async Task<IActionResult> OnPostChangeRoleAsync(Guid memberId, string role)
        {
            if (!FeatureEnabled)
            {
                return NotFound();
            }

            var owner = await GetCurrentOwnerAsync();
            if (owner == null)
            {
                return Forbid();
            }

            if (!SellerInternalRoles.IsValid(role))
            {
                TempData["TeamAlert"] = "Unable to change role: invalid role selected.";
                return RedirectToPage();
            }

            var member = await _dbContext.SellerTeamMembers.FirstOrDefaultAsync(m =>
                m.Id == memberId && m.StoreOwnerId == (owner.StoreOwnerId ?? owner.Id));
            if (member == null || member.Status == SellerInternalUserStatuses.Deactivated)
            {
                TempData["TeamAlert"] = "Unable to change role for this user.";
                return RedirectToPage();
            }

            member.Role = role;
            await UpdateMemberUserRoleAsync(member);
            await _dbContext.SaveChangesAsync();

            TempData["TeamAlert"] = $"Role updated to {role} for {member.Email}.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeactivateAsync(Guid memberId)
        {
            if (!FeatureEnabled)
            {
                return NotFound();
            }

            var owner = await GetCurrentOwnerAsync();
            if (owner == null)
            {
                return Forbid();
            }

            var member = await _dbContext.SellerTeamMembers.FirstOrDefaultAsync(m =>
                m.Id == memberId && m.StoreOwnerId == (owner.StoreOwnerId ?? owner.Id));
            if (member == null)
            {
                TempData["TeamAlert"] = "Could not find that team member.";
                return RedirectToPage();
            }

            member.Status = SellerInternalUserStatuses.Deactivated;
            member.DeactivatedOn = DateTimeOffset.UtcNow;

            if (!string.IsNullOrEmpty(member.AcceptedUserId))
            {
                var user = await _userManager.FindByIdAsync(member.AcceptedUserId);
                if (user != null)
                {
                    await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                    await _userManager.UpdateSecurityStampAsync(user);
                }
            }

            await _dbContext.SaveChangesAsync();
            TempData["TeamAlert"] = $"Deactivated access for {member.Email}.";
            return RedirectToPage();
        }

        private async Task<ApplicationUser?> GetCurrentOwnerAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(user.StoreOwnerId))
            {
                user.StoreOwnerId = user.Id;
                await _userManager.UpdateAsync(user);
            }

            var isOwner = await _userManager.IsInRoleAsync(user, SellerInternalRoles.StoreOwner);
            if (!isOwner && string.Equals(user.AccountType, AccountTypes.Seller, StringComparison.OrdinalIgnoreCase))
            {
                await _userManager.AddToRoleAsync(user, SellerInternalRoles.StoreOwner);
                isOwner = true;
            }

            return isOwner ? user : null;
        }

        private Task<List<SellerTeamMember>> LoadMembersAsync(string ownerId)
        {
            return _dbContext.SellerTeamMembers
                .Where(m => m.StoreOwnerId == ownerId)
                .OrderBy(m => m.Email)
                .ToListAsync();
        }

        private async Task UpdateMemberUserRoleAsync(SellerTeamMember member)
        {
            if (string.IsNullOrEmpty(member.AcceptedUserId))
            {
                return;
            }

            var user = await _userManager.FindByIdAsync(member.AcceptedUserId);
            if (user == null)
            {
                return;
            }

            var roles = await _userManager.GetRolesAsync(user);
            var toRemove = roles.Where(r => SellerInternalRoles.Allowed.Any(a => a.Equals(r, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (toRemove.Length > 0)
            {
                await _userManager.RemoveFromRolesAsync(user, toRemove);
            }

            await _userManager.AddToRoleAsync(user, member.Role);
        }

        private async Task SendInvitationEmailAsync(SellerTeamMember member)
        {
            try
            {
                var callbackUrl = Url.Page(
                    "/Seller/AcceptInvitation",
                    pageHandler: null,
                    values: new { code = member.InvitationCode },
                    protocol: Request.Scheme);

                var message = $"You have been invited to the seller panel as {member.Role}. " +
                              $"Accept the invitation and set your password here: <a href='{HtmlEncoder.Default.Encode(callbackUrl!)}'>join the team</a>.";

                await _emailSender.SendEmailAsync(member.Email, "You're invited to join a store team", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send internal user invite to {Email}", member.Email);
            }
        }
    }
}
