using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public record AdminUserActionResult(bool Success, string Message);

    public class AdminUserActionService
    {
        private readonly ApplicationDbContext _applicationDbContext;
        private readonly ProductDbContext _productDbContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly TimeProvider _timeProvider;
        private readonly CriticalActionAuditService _criticalAudit;
        private readonly ILogger<AdminUserActionService> _logger;

        public AdminUserActionService(
            ApplicationDbContext applicationDbContext,
            ProductDbContext productDbContext,
            UserManager<ApplicationUser> userManager,
            TimeProvider timeProvider,
            CriticalActionAuditService criticalAudit,
            ILogger<AdminUserActionService> logger)
        {
            _applicationDbContext = applicationDbContext;
            _productDbContext = productDbContext;
            _userManager = userManager;
            _timeProvider = timeProvider;
            _criticalAudit = criticalAudit;
            _logger = logger;
        }

        public async Task<AdminUserActionResult> BlockUserAsync(string userId, string? actorUserId, string actorName, string? reason, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return new AdminUserActionResult(false, "User not found.");
            }

            var now = _timeProvider.GetUtcNow();
            var normalizedReason = NormalizeReason(reason);
            var actor = string.IsNullOrWhiteSpace(actorName) ? "Admin" : actorName.Trim();

            user.BlockedOn = now;
            user.BlockedByUserId = actorUserId;
            user.BlockedByName = actor;
            user.BlockReason = normalizedReason;
            user.LockoutEnabled = true;

            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return new AdminUserActionResult(false, "Unable to block this account right now.");
            }

            await _userManager.UpdateSecurityStampAsync(user);

            _applicationDbContext.UserAdminAudits.Add(new UserAdminAudit
            {
                UserId = user.Id,
                ActorUserId = actorUserId,
                ActorName = actor,
                Action = "Blocked",
                Reason = normalizedReason,
                CreatedOn = now
            });

            await _applicationDbContext.SaveChangesAsync(cancellationToken);
            await SetSellerBlockedStateAsync(user.Id, true, cancellationToken);

            _logger.LogInformation("User {UserId} blocked by {Actor}.", user.Id, actor);
            return new AdminUserActionResult(true, "Account blocked.");
        }

        public async Task<AdminUserActionResult> UnblockUserAsync(string userId, string? actorUserId, string actorName, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return new AdminUserActionResult(false, "User not found.");
            }

            var actor = string.IsNullOrWhiteSpace(actorName) ? "Admin" : actorName.Trim();
            var now = _timeProvider.GetUtcNow();

            user.BlockedOn = null;
            user.BlockedByUserId = null;
            user.BlockedByName = null;
            user.BlockReason = null;
            user.LockoutEnabled = true;
            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return new AdminUserActionResult(false, "Unable to unblock this account right now.");
            }

            await _userManager.UpdateSecurityStampAsync(user);

            _applicationDbContext.UserAdminAudits.Add(new UserAdminAudit
            {
                UserId = user.Id,
                ActorUserId = actorUserId,
                ActorName = actor,
                Action = "Reactivated",
                Reason = "Account reactivated",
                CreatedOn = now
            });

            await _applicationDbContext.SaveChangesAsync(cancellationToken);
            await SetSellerBlockedStateAsync(user.Id, false, cancellationToken);

            _logger.LogInformation("User {UserId} reactivated by {Actor}.", user.Id, actor);
            return new AdminUserActionResult(true, "Account reactivated.");
        }

        public async Task<AdminUserActionResult> ChangeRoleAsync(string userId, string targetRole, string? actorUserId, string actorName, CancellationToken cancellationToken = default)
        {
            var normalizedActor = string.IsNullOrWhiteSpace(actorName) ? "Admin" : actorName.Trim();

            if (!PlatformRoles.IsValid(targetRole))
            {
                await _criticalAudit.RecordAsync(
                    new CriticalActionAuditEntry("RoleChange", "User", userId, normalizedActor, actorUserId, false, "Invalid role requested."),
                    cancellationToken);
                return new AdminUserActionResult(false, "Choose a valid platform role.");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                await _criticalAudit.RecordAsync(
                    new CriticalActionAuditEntry("RoleChange", "User", userId, normalizedActor, actorUserId, false, "User not found."),
                    cancellationToken);
                return new AdminUserActionResult(false, "User not found.");
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            var platformRoles = currentRoles.Where(PlatformRoles.IsValid).ToList();

            if (platformRoles.Count > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, platformRoles);
                if (!removeResult.Succeeded)
                {
                    await _criticalAudit.RecordAsync(
                        new CriticalActionAuditEntry("RoleChange", "User", user.Id, normalizedActor, actorUserId, false, "Unable to remove existing roles."),
                        cancellationToken);
                    return new AdminUserActionResult(false, "Unable to update roles right now.");
                }
            }

            var addResult = await _userManager.AddToRoleAsync(user, targetRole);
            if (!addResult.Succeeded)
            {
                await _criticalAudit.RecordAsync(
                    new CriticalActionAuditEntry("RoleChange", "User", user.Id, normalizedActor, actorUserId, false, "Unable to assign requested role."),
                    cancellationToken);
                return new AdminUserActionResult(false, "Unable to assign the requested role.");
            }

            user.AccountType = ResolveAccountType(targetRole);
            if (!string.Equals(targetRole, PlatformRoles.Seller, StringComparison.OrdinalIgnoreCase))
            {
                user.StoreOwnerId = null;
                var sellerRoles = SellerInternalRoles.Allowed.ToList();
                if (sellerRoles.Count > 0)
                {
                    await _userManager.RemoveFromRolesAsync(user, sellerRoles);
                }
            }
            else
            {
                if (user.StoreOwnerId == null)
                {
                    user.StoreOwnerId = user.Id;
                }

                if (!await _userManager.IsInRoleAsync(user, SellerInternalRoles.StoreOwner))
                {
                    await _userManager.AddToRoleAsync(user, SellerInternalRoles.StoreOwner);
                }
            }

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                await _criticalAudit.RecordAsync(
                    new CriticalActionAuditEntry("RoleChange", "User", user.Id, normalizedActor, actorUserId, false, "Unable to persist updated role."),
                    cancellationToken);
                return new AdminUserActionResult(false, "Unable to persist the updated role.");
            }

            await _userManager.UpdateSecurityStampAsync(user);

            _applicationDbContext.UserAdminAudits.Add(new UserAdminAudit
            {
                UserId = user.Id,
                ActorUserId = actorUserId,
                ActorName = normalizedActor,
                Action = "Role changed",
                Reason = $"Role set to {targetRole}",
                CreatedOn = _timeProvider.GetUtcNow()
            });

            await _applicationDbContext.SaveChangesAsync(cancellationToken);

            await _criticalAudit.RecordAsync(
                new CriticalActionAuditEntry("RoleChange", "User", user.Id, normalizedActor, actorUserId, true, $"Role set to {targetRole}"),
                cancellationToken);

            _logger.LogInformation("User {UserId} role updated to {Role} by {Actor}.", user.Id, targetRole, normalizedActor);
            return new AdminUserActionResult(true, $"Role changed to {targetRole}.");
        }

        private static string? NormalizeReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return null;
            }

            var trimmed = reason.Trim();
            return trimmed.Length > 512 ? trimmed[..512] : trimmed;
        }

        private async Task SetSellerBlockedStateAsync(string sellerId, bool isBlocked, CancellationToken cancellationToken)
        {
            await _productDbContext.Products
                .Where(p => p.SellerId == sellerId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.IsSellerBlocked, isBlocked), cancellationToken);
        }

        public async Task RecordUserAccessAsync(
            string userId,
            string? actorUserId,
            string actorName,
            string action,
            string? reason = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(action))
            {
                return;
            }

            var normalizedActor = string.IsNullOrWhiteSpace(actorName) ? "Admin" : actorName.Trim();
            _applicationDbContext.UserAdminAudits.Add(new UserAdminAudit
            {
                UserId = userId.Trim(),
                ActorUserId = actorUserId,
                ActorName = normalizedActor,
                Action = action.Trim(),
                Reason = NormalizeReason(reason),
                CreatedOn = _timeProvider.GetUtcNow()
            });

            await _applicationDbContext.SaveChangesAsync(cancellationToken);
        }

        private static string ResolveAccountType(string targetRole) =>
            targetRole switch
            {
                var r when r.Equals(PlatformRoles.Buyer, StringComparison.OrdinalIgnoreCase) => AccountTypes.Buyer,
                var r when r.Equals(PlatformRoles.Seller, StringComparison.OrdinalIgnoreCase) => AccountTypes.Seller,
                _ => AccountTypes.Admin
            };
    }
}
