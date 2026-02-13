using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public record ProductPhotoModerationItem(
        int Id,
        int ProductId,
        string ProductTitle,
        string MerchantSku,
        string SellerId,
        string SellerName,
        string? SellerEmail,
        string Url,
        string? ThumbnailUrl,
        bool IsMain,
        string Status,
        string? FlaggedReason,
        string? ModerationNote,
        DateTimeOffset CreatedOn,
        DateTimeOffset? FlaggedOn,
        DateTimeOffset? ReviewedOn);

    public record ProductPhotoAuditView(
        int Id,
        string Action,
        string? Actor,
        string? Reason,
        string FromStatus,
        string ToStatus,
        DateTimeOffset CreatedOn);

    public class ProductPhotoModerationFilters
    {
        public List<string> Statuses { get; set; } = new();

        public bool FlaggedOnly { get; set; } = true;
    }

    public record ProductPhotoModerationResult(bool Success, int UpdatedCount, string? Error = null);

    public class ProductPhotoModerationService
    {
        private readonly ProductDbContext _productDbContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationService? _notificationService;

        public ProductPhotoModerationService(
            ProductDbContext productDbContext,
            UserManager<ApplicationUser> userManager,
            NotificationService? notificationService = null)
        {
            _productDbContext = productDbContext;
            _userManager = userManager;
            _notificationService = notificationService;
        }

        public async Task SyncFromProductAsync(ProductModel product, CancellationToken cancellationToken = default)
        {
            var existing = await _productDbContext.ProductPhotos
                .Where(p => p.ProductId == product.Id)
                .ToListAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var sources = BuildPhotoSources(product);
            var currentUrls = new HashSet<string>(sources.Select(p => p.Url), StringComparer.OrdinalIgnoreCase);

            foreach (var orphan in existing.Where(p => !currentUrls.Contains(p.Url) && p.Status != ProductPhotoStatuses.Removed))
            {
                var previousStatus = ProductPhotoStatuses.Normalize(orphan.Status);
                orphan.Status = ProductPhotoStatuses.Removed;
                orphan.ModerationNote ??= "Removed from listing";
                orphan.ReviewedBy = "System";
                orphan.ReviewedOn = now;
                AddAudit(orphan, "RemovedFromListing", "System", orphan.ModerationNote, previousStatus, ProductPhotoStatuses.Removed, now);
            }

            foreach (var photo in sources)
            {
                var current = existing.FirstOrDefault(p => string.Equals(p.Url, photo.Url, StringComparison.OrdinalIgnoreCase));
                if (current == null)
                {
                    _productDbContext.ProductPhotos.Add(new ProductPhoto
                    {
                        ProductId = product.Id,
                        Url = photo.Url,
                        ThumbnailUrl = photo.ThumbnailUrl,
                        IsMain = photo.IsMain,
                        Status = ProductPhotoStatuses.Approved,
                        CreatedOn = now,
                        ReviewedOn = now
                    });
                    continue;
                }

                current.IsMain = current.IsMain || photo.IsMain;
                if (string.IsNullOrWhiteSpace(current.ThumbnailUrl) && !string.IsNullOrWhiteSpace(photo.ThumbnailUrl))
                {
                    current.ThumbnailUrl = photo.ThumbnailUrl;
                }
            }

            await _productDbContext.SaveChangesAsync(cancellationToken);
            await RefreshProductImagesAsync(product.Id, cancellationToken);
        }

        public async Task<ProductPhotoModerationResult> FlagAsync(
            int productId,
            string url,
            string actor,
            string? reason = null,
            bool isMain = false,
            string? thumbnailUrl = null,
            CancellationToken cancellationToken = default)
        {
            var product = await _productDbContext.Products
                .FirstOrDefaultAsync(p => p.Id == productId && p.WorkflowState != ProductWorkflowStates.Archived, cancellationToken);

            if (product == null)
            {
                return new ProductPhotoModerationResult(false, 0, "Product not found.");
            }

            var normalizedUrl = NormalizeUrl(url);
            if (string.IsNullOrWhiteSpace(normalizedUrl))
            {
                return new ProductPhotoModerationResult(false, 0, "Invalid photo URL.");
            }

            var normalizedActor = NormalizeActor(actor);
            var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Flagged for review" : reason.Trim();
            var now = DateTimeOffset.UtcNow;

            var photo = await _productDbContext.ProductPhotos
                .FirstOrDefaultAsync(p => p.ProductId == productId && p.Url == normalizedUrl, cancellationToken);

            var isNew = false;
            if (photo == null)
            {
                photo = new ProductPhoto
                {
                    ProductId = productId,
                    Url = normalizedUrl,
                    ThumbnailUrl = thumbnailUrl,
                    IsMain = isMain,
                    Status = ProductPhotoStatuses.Pending,
                    CreatedOn = now
                };
                _productDbContext.ProductPhotos.Add(photo);
                isNew = true;
            }

            var previousStatus = ProductPhotoStatuses.Normalize(photo.Status);
            photo.Status = ProductPhotoStatuses.Pending;
            photo.FlaggedBy = normalizedActor;
            photo.FlaggedReason = normalizedReason;
            photo.FlaggedOn = now;
            photo.ReviewedBy = null;
            photo.ReviewedOn = null;
            if (!string.IsNullOrWhiteSpace(thumbnailUrl))
            {
                photo.ThumbnailUrl = thumbnailUrl;
            }

            if (isNew)
            {
                await _productDbContext.SaveChangesAsync(cancellationToken);
            }

            AddAudit(photo, "Flagged", normalizedActor, normalizedReason, previousStatus, ProductPhotoStatuses.Pending, now);
            await _productDbContext.SaveChangesAsync(cancellationToken);
            await RefreshProductImagesAsync(productId, cancellationToken);

            return new ProductPhotoModerationResult(true, 1, null);
        }

        public async Task<ProductPhotoModerationResult> ApproveAsync(IEnumerable<int> photoIds, string actor, string? note = null, CancellationToken cancellationToken = default)
        {
            var ids = NormalizeIds(photoIds);
            if (ids.Count == 0)
            {
                return new ProductPhotoModerationResult(false, 0, "Select at least one photo.");
            }

            var normalizedActor = NormalizeActor(actor);
            var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            var now = DateTimeOffset.UtcNow;
            var updated = 0;
            var productIds = new HashSet<int>();

            var photos = await _productDbContext.ProductPhotos
                .Where(p => ids.Contains(p.Id))
                .ToListAsync(cancellationToken);

            foreach (var photo in photos)
            {
                var previousStatus = ProductPhotoStatuses.Normalize(photo.Status);
                photo.Status = ProductPhotoStatuses.Approved;
                photo.ModerationNote = normalizedNote ?? photo.ModerationNote;
                photo.ReviewedBy = normalizedActor;
                photo.ReviewedOn = now;
                AddAudit(photo, "Approved", normalizedActor, normalizedNote, previousStatus, ProductPhotoStatuses.Approved, now);
                productIds.Add(photo.ProductId);
                updated++;
            }

            await _productDbContext.SaveChangesAsync(cancellationToken);
            foreach (var productId in productIds)
            {
                await RefreshProductImagesAsync(productId, cancellationToken);
            }

            return new ProductPhotoModerationResult(updated > 0, updated, null);
        }

        public async Task<ProductPhotoModerationResult> RemoveAsync(IEnumerable<int> photoIds, string actor, string reason, CancellationToken cancellationToken = default)
        {
            var ids = NormalizeIds(photoIds);
            if (ids.Count == 0)
            {
                return new ProductPhotoModerationResult(false, 0, "Select at least one photo.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return new ProductPhotoModerationResult(false, 0, "Provide a removal reason.");
            }

            var normalizedActor = NormalizeActor(actor);
            var normalizedReason = reason.Trim();
            var now = DateTimeOffset.UtcNow;
            var updated = 0;
            var productIds = new HashSet<int>();

            var photos = await _productDbContext.ProductPhotos
                .Where(p => ids.Contains(p.Id))
                .ToListAsync(cancellationToken);

            foreach (var photo in photos)
            {
                var previousStatus = ProductPhotoStatuses.Normalize(photo.Status);
                photo.Status = ProductPhotoStatuses.Removed;
                photo.ModerationNote = normalizedReason;
                photo.ReviewedBy = normalizedActor;
                photo.ReviewedOn = now;
                AddAudit(photo, "Removed", normalizedActor, normalizedReason, previousStatus, ProductPhotoStatuses.Removed, now);
                productIds.Add(photo.ProductId);
                updated++;
            }

            await _productDbContext.SaveChangesAsync(cancellationToken);
            foreach (var productId in productIds)
            {
                await RefreshProductImagesAsync(productId, cancellationToken);
                await NotifySellerAsync(productId, normalizedReason, cancellationToken);
            }

            return new ProductPhotoModerationResult(updated > 0, updated, null);
        }

        public async Task<PagedResult<ProductPhotoModerationItem>> GetQueueAsync(
            ProductPhotoModerationFilters? filters = null,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var normalizedFilters = NormalizeFilters(filters);
            var limit = pageSize <= 0 ? 20 : Math.Min(pageSize, 50);
            var source = _productDbContext.ProductPhotos.AsNoTracking().AsQueryable();

            if (normalizedFilters.Statuses.Count > 0)
            {
                source = source.Where(p => normalizedFilters.Statuses.Contains(p.Status));
            }
            else
            {
                source = source.Where(p => p.Status == ProductPhotoStatuses.Pending);
            }

            if (normalizedFilters.FlaggedOnly)
            {
                source = source.Where(p => p.FlaggedOn != null || p.FlaggedReason != null || p.Status == ProductPhotoStatuses.Pending);
            }

            var totalCount = await source.CountAsync(cancellationToken);
            var totalPages = limit <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)limit);
            var pageNumber = totalPages == 0 ? 1 : Math.Clamp(page, 1, totalPages);

            var photos = await source
                .OrderBy(p => p.Status == ProductPhotoStatuses.Pending ? 0 : 1)
                .ThenByDescending(p => p.FlaggedOn ?? p.CreatedOn)
                .Skip((pageNumber - 1) * limit)
                .Take(limit)
                .ToListAsync(cancellationToken);

            var productIds = photos.Select(ph => ph.ProductId).Distinct().ToList();
            var productLookup = await _productDbContext.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            var sellerLookup = await LoadSellersAsync(productLookup.Values.Select(p => p.SellerId).Distinct().ToList(), cancellationToken);

            var items = photos.Select(photo =>
            {
                productLookup.TryGetValue(photo.ProductId, out var product);
                var sellerId = product?.SellerId ?? string.Empty;
                sellerLookup.TryGetValue(sellerId, out var seller);
                var sellerName = string.IsNullOrWhiteSpace(seller.Name) ? "Seller" : seller.Name;
                return new ProductPhotoModerationItem(
                    photo.Id,
                    photo.ProductId,
                    product?.Title ?? "Product",
                    product?.MerchantSku ?? string.Empty,
                    sellerId,
                    sellerName,
                    seller.Email,
                    photo.Url,
                    photo.ThumbnailUrl,
                    photo.IsMain,
                    ProductPhotoStatuses.Normalize(photo.Status),
                    photo.FlaggedReason,
                    photo.ModerationNote,
                    photo.CreatedOn,
                    photo.FlaggedOn,
                    photo.ReviewedOn);
            }).ToList();

            return new PagedResult<ProductPhotoModerationItem>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = limit
            };
        }

        public async Task<List<ProductPhotoAuditView>> GetAuditAsync(int photoId, CancellationToken cancellationToken = default)
        {
            var audits = await _productDbContext.ProductPhotoModerationAudits.AsNoTracking()
                .Where(a => a.PhotoId == photoId)
                .OrderByDescending(a => a.CreatedOn)
                .Take(50)
                .ToListAsync(cancellationToken);

            return audits
                .Select(a => new ProductPhotoAuditView(
                    a.Id,
                    a.Action,
                    a.Actor,
                    a.Reason,
                    a.FromStatus,
                    a.ToStatus,
                    a.CreatedOn))
                .ToList();
        }

        private async Task RefreshProductImagesAsync(int productId, CancellationToken cancellationToken)
        {
            var product = await _productDbContext.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
            if (product == null)
            {
                return;
            }

            var approved = await _productDbContext.ProductPhotos.AsNoTracking()
                .Where(p => p.ProductId == productId && p.Status == ProductPhotoStatuses.Approved)
                .OrderByDescending(p => p.IsMain)
                .ThenBy(p => p.Id)
                .ToListAsync(cancellationToken);

            var main = approved.FirstOrDefault(p => p.IsMain) ?? approved.FirstOrDefault();
            var gallery = approved.Where(p => main == null || p.Id != main.Id).Select(p => p.Url).ToList();

            product.MainImageUrl = main?.Url;
            product.GalleryImageUrls = gallery.Any() ? string.Join(", ", gallery) : null;

            await _productDbContext.SaveChangesAsync(cancellationToken);
        }

        private static List<(string Url, string? ThumbnailUrl, bool IsMain)> BuildPhotoSources(ProductModel product)
        {
            var photos = new List<(string Url, string? ThumbnailUrl, bool IsMain)>();

            if (!string.IsNullOrWhiteSpace(product.MainImageUrl))
            {
                photos.Add((product.MainImageUrl, null, true));
            }

            if (!string.IsNullOrWhiteSpace(product.GalleryImageUrls))
            {
                photos.AddRange(product.GalleryImageUrls
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(url => (url, (string?)null, false)));
            }

            return photos;
        }

        private static List<int> NormalizeIds(IEnumerable<int>? ids)
        {
            return ids?
                .Where(id => id > 0)
                .Distinct()
                .ToList() ?? new List<int>();
        }

        private static ProductPhotoModerationFilters NormalizeFilters(ProductPhotoModerationFilters? filters)
        {
            var statuses = filters?.Statuses?
                .Select(ProductPhotoStatuses.Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            return new ProductPhotoModerationFilters
            {
                Statuses = statuses,
                FlaggedOnly = filters?.FlaggedOnly ?? true
            };
        }

        private static string NormalizeActor(string actor)
        {
            return string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim();
        }

        private static string NormalizeUrl(string url)
        {
            return url?.Trim() ?? string.Empty;
        }

        private void AddAudit(ProductPhoto photo, string action, string actor, string? reason, string fromStatus, string toStatus, DateTimeOffset now)
        {
            _productDbContext.ProductPhotoModerationAudits.Add(new ProductPhotoModerationAudit
            {
                PhotoId = photo.Id,
                Action = action,
                Actor = actor,
                Reason = reason,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                CreatedOn = now
            });
        }

        private async Task NotifySellerAsync(int productId, string reason, CancellationToken cancellationToken)
        {
            if (_notificationService == null)
            {
                return;
            }

            var product = await _productDbContext.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

            if (product == null)
            {
                return;
            }

            var title = $"Photo removed for {product.Title}";
            var description = string.IsNullOrWhiteSpace(reason)
                ? "A product photo was removed by moderators."
                : reason;

            await _notificationService.AddNotificationAsync(
                product.SellerId,
                title,
                description,
                $"/Seller/Products/Edit/{product.Id}",
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

            var query = _userManager.Users.Where(u => sellerIds.Contains(u.Id));

            List<(string Id, string? FullName, string? BusinessName, string? Email)> users;
            if (query is IAsyncEnumerable<ApplicationUser>)
            {
                users = (await query
                    .Select(u => new { u.Id, u.FullName, u.BusinessName, u.Email })
                    .ToListAsync(cancellationToken))
                    .Select(u => (u.Id, (string?)u.FullName, u.BusinessName, u.Email))
                    .ToList();
            }
            else
            {
                users = query
                    .Select(u => new { u.Id, u.FullName, u.BusinessName, u.Email })
                    .ToList()
                    .Select(u => (u.Id, (string?)u.FullName, u.BusinessName, u.Email))
                    .ToList();
            }

            foreach (var user in users)
            {
                var sellerName = string.IsNullOrWhiteSpace(user.BusinessName) ? (user.FullName ?? "Seller") : user.BusinessName!;
                lookup[user.Id] = (sellerName, user.Email);
            }

            return lookup;
        }
    }
}
