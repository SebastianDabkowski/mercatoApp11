using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public record ProductModerationItem(
        int Id,
        string Title,
        string MerchantSku,
        string SellerId,
        string SellerName,
        string? SellerEmail,
        string Category,
        int? CategoryId,
        string WorkflowState,
        string ModerationStatus,
        string? ModerationNote,
        string? MainImageUrl,
        string? GalleryImageUrls,
        string? Description,
        decimal Price,
        int Stock,
        DateTimeOffset? LastModeratedOn);

    public record ProductModerationAuditView(
        int Id,
        string Action,
        string? Actor,
        string? Reason,
        string FromStatus,
        string ToStatus,
        DateTimeOffset CreatedOn);

    public class ProductModerationFilters
    {
        public List<string> Statuses { get; set; } = new();

        public int? CategoryId { get; set; }
    }

    public record ProductModerationActionResult(bool Success, int UpdatedCount, string? Error = null);

    public class ProductModerationService
    {
        private readonly ProductDbContext _productDbContext;
        private readonly ChangeProductWorkflowState _workflow;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationService? _notificationService;

        public ProductModerationService(
            ProductDbContext productDbContext,
            ChangeProductWorkflowState workflow,
            UserManager<ApplicationUser> userManager,
            NotificationService? notificationService = null)
        {
            _productDbContext = productDbContext;
            _workflow = workflow;
            _userManager = userManager;
            _notificationService = notificationService;
        }

        public async Task<ProductModerationActionResult> QueueForReviewAsync(int productId, string actor, CancellationToken cancellationToken = default)
        {
            var product = await _productDbContext.Products
                .FirstOrDefaultAsync(p => p.Id == productId && p.WorkflowState != ProductWorkflowStates.Archived, cancellationToken);

            if (product == null)
            {
                return new ProductModerationActionResult(false, 0, "Product not found.");
            }

            var normalizedActor = NormalizeActor(actor);
            var now = DateTimeOffset.UtcNow;
            var previousStatus = ProductModerationStatuses.Normalize(product.ModerationStatus);

            product.ModerationStatus = ProductModerationStatuses.Pending;
            product.ModerationNote = null;
            product.LastModeratedBy = normalizedActor;
            product.LastModeratedOn = now;

            AddAudit(product, "Queued", normalizedActor, null, previousStatus, ProductModerationStatuses.Pending, now);

            var result = await _workflow.SetStateAsync(product, ProductWorkflowStates.Pending);
            if (!result.Succeeded)
            {
                return new ProductModerationActionResult(false, 0, string.Join(" ", result.Errors));
            }

            return new ProductModerationActionResult(true, 1, null);
        }

        public async Task<ProductModerationActionResult> ApproveAsync(IEnumerable<int> productIds, string actor, string? note = null, CancellationToken cancellationToken = default)
        {
            var ids = NormalizeIds(productIds);
            if (ids.Count == 0)
            {
                return new ProductModerationActionResult(false, 0, "Select at least one product.");
            }

            var normalizedActor = NormalizeActor(actor);
            var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            var now = DateTimeOffset.UtcNow;

            var failures = new List<string>();
            var updated = 0;

            foreach (var id in ids)
            {
                var product = await _productDbContext.Products
                    .FirstOrDefaultAsync(p => p.Id == id && p.WorkflowState != ProductWorkflowStates.Archived, cancellationToken);

                if (product == null)
                {
                    failures.Add($"Product {id} not found.");
                    continue;
                }

                var previousStatus = ProductModerationStatuses.Normalize(product.ModerationStatus);
                product.ModerationStatus = ProductModerationStatuses.Approved;
                product.ModerationNote = normalizedNote ?? product.ModerationNote;
                product.LastModeratedBy = normalizedActor;
                product.LastModeratedOn = now;
                AddAudit(product, "Approved", normalizedActor, normalizedNote, previousStatus, ProductModerationStatuses.Approved, now);

                var result = await _workflow.SetStateAsync(product, ProductWorkflowStates.Active, isAdminOverride: true);
                if (!result.Succeeded)
                {
                    failures.Add($"Product {product.Id}: {string.Join(" ", result.Errors)}");
                    continue;
                }

                updated++;
            }

            return new ProductModerationActionResult(updated > 0, updated, failures.Count > 0 ? string.Join(" ", failures) : null);
        }

        public async Task<ProductModerationActionResult> RejectAsync(IEnumerable<int> productIds, string actor, string reason, CancellationToken cancellationToken = default)
        {
            var ids = NormalizeIds(productIds);
            if (ids.Count == 0)
            {
                return new ProductModerationActionResult(false, 0, "Select at least one product.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return new ProductModerationActionResult(false, 0, "Provide a rejection reason.");
            }

            var normalizedActor = NormalizeActor(actor);
            var normalizedReason = reason.Trim();
            var now = DateTimeOffset.UtcNow;

            var failures = new List<string>();
            var updated = 0;

            foreach (var id in ids)
            {
                var product = await _productDbContext.Products
                    .FirstOrDefaultAsync(p => p.Id == id && p.WorkflowState != ProductWorkflowStates.Archived, cancellationToken);

                if (product == null)
                {
                    failures.Add($"Product {id} not found.");
                    continue;
                }

                var previousStatus = ProductModerationStatuses.Normalize(product.ModerationStatus);
                product.ModerationStatus = ProductModerationStatuses.Rejected;
                product.ModerationNote = normalizedReason;
                product.LastModeratedBy = normalizedActor;
                product.LastModeratedOn = now;
                AddAudit(product, "Rejected", normalizedActor, normalizedReason, previousStatus, ProductModerationStatuses.Rejected, now);

                var result = await _workflow.SetStateAsync(product, ProductWorkflowStates.Rejected, isAdminOverride: true);
                if (!result.Succeeded)
                {
                    failures.Add($"Product {product.Id}: {string.Join(" ", result.Errors)}");
                    continue;
                }

                updated++;
                await NotifySellerAsync(product, normalizedReason, cancellationToken);
            }

            return new ProductModerationActionResult(updated > 0, updated, failures.Count > 0 ? string.Join(" ", failures) : null);
        }

        public async Task<PagedResult<ProductModerationItem>> GetQueueAsync(ProductModerationFilters? filters = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
        {
            var normalizedFilters = NormalizeFilters(filters);
            var limit = pageSize <= 0 ? 20 : Math.Min(pageSize, 50);

            var source = _productDbContext.Products.AsNoTracking()
                .Where(p => p.WorkflowState != ProductWorkflowStates.Archived);

            if (normalizedFilters.Statuses.Count > 0)
            {
                source = source.Where(p => normalizedFilters.Statuses.Contains(p.ModerationStatus));
            }

            if (normalizedFilters.CategoryId.HasValue)
            {
                source = source.Where(p => p.CategoryId == normalizedFilters.CategoryId.Value);
            }

            var totalCount = await source.CountAsync(cancellationToken);
            var totalPages = limit <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)limit);
            var pageNumber = totalPages == 0 ? 1 : Math.Clamp(page, 1, totalPages);

            var items = await source
                .OrderBy(p => p.ModerationStatus == ProductModerationStatuses.Pending ? 0 : 1)
                .ThenByDescending(p => p.Id)
                .Skip((pageNumber - 1) * limit)
                .Take(limit)
                .ToListAsync(cancellationToken);

            var sellerLookup = await LoadSellersAsync(items.Select(p => p.SellerId).Distinct().ToList(), cancellationToken);

            var results = items.Select(p =>
            {
                sellerLookup.TryGetValue(p.SellerId, out var seller);
                var sellerName = string.IsNullOrWhiteSpace(seller.Name) ? "Seller" : seller.Name;
                var sellerEmail = seller.Email;
                var status = ProductModerationStatuses.Normalize(p.ModerationStatus);
                return new ProductModerationItem(
                    p.Id,
                    p.Title,
                    p.MerchantSku,
                    p.SellerId,
                    sellerName,
                    sellerEmail,
                    p.Category,
                    p.CategoryId,
                    p.WorkflowState,
                    status,
                    p.ModerationNote,
                    p.MainImageUrl,
                    p.GalleryImageUrls,
                    p.Description,
                    p.Price,
                    p.Stock,
                    p.LastModeratedOn);
            }).ToList();

            return new PagedResult<ProductModerationItem>
            {
                Items = results,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = limit
            };
        }

        public async Task<List<ProductModerationAuditView>> GetAuditAsync(int productId, CancellationToken cancellationToken = default)
        {
            var audits = await _productDbContext.ProductModerationAudits.AsNoTracking()
                .Where(a => a.ProductId == productId)
                .OrderByDescending(a => a.CreatedOn)
                .Take(50)
                .ToListAsync(cancellationToken);

            return audits
                .Select(a => new ProductModerationAuditView(
                    a.Id,
                    a.Action,
                    a.Actor,
                    a.Reason,
                    a.FromStatus,
                    a.ToStatus,
                    a.CreatedOn))
                .ToList();
        }

        private static List<int> NormalizeIds(IEnumerable<int>? ids)
        {
            return ids?
                .Where(id => id > 0)
                .Distinct()
                .ToList() ?? new List<int>();
        }

        private static ProductModerationFilters NormalizeFilters(ProductModerationFilters? filters)
        {
            var statuses = filters?.Statuses?
                .Select(ProductModerationStatuses.Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            return new ProductModerationFilters
            {
                Statuses = statuses,
                CategoryId = filters?.CategoryId
            };
        }

        private static string NormalizeActor(string actor)
        {
            return string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim();
        }

        private void AddAudit(ProductModel product, string action, string actor, string? reason, string fromStatus, string toStatus, DateTimeOffset now)
        {
            _productDbContext.ProductModerationAudits.Add(new ProductModerationAudit
            {
                ProductId = product.Id,
                Action = action,
                Actor = actor,
                Reason = reason,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                CreatedOn = now
            });
        }

        private async Task NotifySellerAsync(ProductModel product, string reason, CancellationToken cancellationToken)
        {
            if (_notificationService == null)
            {
                return;
            }

            var title = $"Product rejected: {product.Title}";
            var description = string.IsNullOrWhiteSpace(reason)
                ? "Your product was rejected by moderators."
                : reason;

            await _notificationService.AddNotificationAsync(
                product.SellerId,
                title,
                description,
                "/Seller/Products/List",
                "Products",
                cancellationToken);
        }

        private async Task<Dictionary<string, (string Name, string? Email)>> LoadSellersAsync(List<string> sellerIds, CancellationToken cancellationToken)
        {
            var lookup = new Dictionary<string, (string Name, string? Email)>(StringComparer.OrdinalIgnoreCase);
            if (!sellerIds.Any())
            {
                return lookup;
            }

            var users = await _userManager.Users
                .Where(u => sellerIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.BusinessName, u.Email })
                .ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                lookup[user.Id] = (string.IsNullOrWhiteSpace(user.BusinessName) ? user.FullName : user.BusinessName, user.Email);
            }

            return lookup;
        }
    }
}
