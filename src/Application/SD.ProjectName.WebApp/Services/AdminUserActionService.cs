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
        private readonly ILogger<AdminUserActionService> _logger;

        public AdminUserActionService(
            ApplicationDbContext applicationDbContext,
            ProductDbContext productDbContext,
            UserManager<ApplicationUser> userManager,
            TimeProvider timeProvider,
            ILogger<AdminUserActionService> logger)
        {
            _applicationDbContext = applicationDbContext;
            _productDbContext = productDbContext;
            _userManager = userManager;
            _timeProvider = timeProvider;
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
    }
}
