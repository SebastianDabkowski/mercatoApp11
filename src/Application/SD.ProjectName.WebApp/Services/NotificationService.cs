using System.Collections.Concurrent;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public enum NotificationFilter
    {
        All,
        Unread,
        Read
    }

    public static class NotificationFilterOptions
    {
        public const string All = "all";
        public const string Unread = "unread";
        public const string Read = "read";

        public static string Normalize(string? value)
        {
            if (string.Equals(value, Read, StringComparison.OrdinalIgnoreCase))
            {
                return Read;
            }

            if (string.Equals(value, All, StringComparison.OrdinalIgnoreCase))
            {
                return All;
            }

            return Unread;
        }

        public static NotificationFilter ToFilter(string? value) =>
            Normalize(value) switch
            {
                Read => NotificationFilter.Read,
                All => NotificationFilter.All,
                _ => NotificationFilter.Unread
            };
    }

    public class NotificationItem
    {
        public Guid Id { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string TargetUrl { get; init; } = "/";

        public string Category { get; init; } = "System";

        public DateTimeOffset CreatedOn { get; init; }

        public bool IsRead { get; set; }
    }

    public class NotificationFeed
    {
        public required PagedResult<NotificationItem> Items { get; init; }

        public int UnreadCount { get; init; }
    }

    public class NotificationService
    {
        private const int MaxPageSize = 50;
        private readonly TimeProvider _timeProvider;
        private readonly IPushNotificationDispatcher? _pushDispatcher;
        private readonly ConcurrentDictionary<string, List<NotificationItem>> _store = new(StringComparer.OrdinalIgnoreCase);

        public NotificationService(TimeProvider timeProvider, IPushNotificationDispatcher? pushDispatcher = null)
        {
            _timeProvider = timeProvider;
            _pushDispatcher = pushDispatcher;
        }

        public Task<NotificationFeed> GetFeedAsync(
            string userId,
            string? accountType,
            NotificationFilter filter,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(userId));
            }

            pageNumber = Math.Max(1, pageNumber);
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

            var all = GetOrSeed(userId, accountType);
            cancellationToken.ThrowIfCancellationRequested();

            List<NotificationItem> snapshot;
            int unreadCount;
            lock (all)
            {
                snapshot = all.Select(Clone).ToList();
                unreadCount = all.Count(n => !n.IsRead);
            }

            IEnumerable<NotificationItem> filtered = snapshot.OrderByDescending(n => n.CreatedOn);
            filtered = filter switch
            {
                NotificationFilter.Unread => filtered.Where(n => !n.IsRead),
                NotificationFilter.Read => filtered.Where(n => n.IsRead),
                _ => filtered
            };

            var totalCount = filtered.Count();
            var items = filtered
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var paged = new PagedResult<NotificationItem>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };

            return Task.FromResult(new NotificationFeed
            {
                Items = paged,
                UnreadCount = unreadCount
            });
        }

        public Task<int> GetUnreadCountAsync(string userId, string? accountType = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(0);
            }

            var all = GetOrSeed(userId, accountType);
            cancellationToken.ThrowIfCancellationRequested();

            lock (all)
            {
                return Task.FromResult(all.Count(n => !n.IsRead));
            }
        }

        public Task<bool> MarkAsReadAsync(string userId, Guid notificationId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(false);
            }

            var all = GetOrSeed(userId, null);
            cancellationToken.ThrowIfCancellationRequested();

            lock (all)
            {
                var notification = all.FirstOrDefault(n => n.Id == notificationId);
                if (notification == null)
                {
                    return Task.FromResult(false);
                }

                notification.IsRead = true;
                return Task.FromResult(true);
            }
        }

        public Task<NotificationItem> AddNotificationAsync(
            string userId,
            string title,
            string description,
            string targetUrl,
            string category = "Messages",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(userId));
            }

            var notification = new NotificationItem
            {
                Id = Guid.NewGuid(),
                Title = title?.Trim() ?? "Notification",
                Description = description?.Trim() ?? string.Empty,
                TargetUrl = string.IsNullOrWhiteSpace(targetUrl) ? "/" : targetUrl.Trim(),
                Category = string.IsNullOrWhiteSpace(category) ? "Messages" : category.Trim(),
                CreatedOn = _timeProvider.GetUtcNow(),
                IsRead = false
            };

            var all = GetOrSeed(userId, null);
            cancellationToken.ThrowIfCancellationRequested();

            lock (all)
            {
                all.Add(notification);
            }

            if (_pushDispatcher != null)
            {
                _ = _pushDispatcher.DispatchAsync(userId, Clone(notification), cancellationToken);
            }

            return Task.FromResult(notification);
        }

        private List<NotificationItem> GetOrSeed(string userId, string? accountType)
        {
            return _store.GetOrAdd(userId, _ => SeedNotifications(accountType));
        }

        private List<NotificationItem> SeedNotifications(string? accountType)
        {
            var now = _timeProvider.GetUtcNow();
            var items = new List<NotificationItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Title = "System update",
                    Description = "We refreshed your dashboard experience.",
                    TargetUrl = "/Index",
                    Category = "System",
                    CreatedOn = now.AddHours(-12),
                    IsRead = true
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Title = "Security reminder",
                    Description = "Protect your account with strong passwords and two-factor authentication.",
                    TargetUrl = "/Identity/Account/Manage/TwoFactorAuthentication",
                    Category = "System",
                    CreatedOn = now.AddHours(-30),
                    IsRead = true
                }
            };

            items.AddRange(
                accountType switch
                {
                    var t when string.Equals(t, AccountTypes.Seller, StringComparison.OrdinalIgnoreCase) => CreateSellerSeeds(now),
                    var t when string.Equals(t, AccountTypes.Admin, StringComparison.OrdinalIgnoreCase) => CreateAdminSeeds(now),
                    _ => CreateBuyerSeeds(now)
                });

            return items;
        }

        private static IEnumerable<NotificationItem> CreateBuyerSeeds(DateTimeOffset now)
        {
            return new[]
            {
                new NotificationItem
                {
                    Id = Guid.NewGuid(),
                    Title = "Order shipped",
                    Description = "Your latest order is on the way. Track it from your orders page.",
                    TargetUrl = "/Buyer/Orders/Index",
                    Category = "Orders",
                    CreatedOn = now.AddHours(-2),
                    IsRead = false
                },
                new NotificationItem
                {
                    Id = Guid.NewGuid(),
                    Title = "Return update",
                    Description = "Your return request was acknowledged by the seller.",
                    TargetUrl = "/Buyer/Cases/Index",
                    Category = "Returns",
                    CreatedOn = now.AddHours(-5),
                    IsRead = false
                },
                new NotificationItem
                {
                    Id = Guid.NewGuid(),
                    Title = "Message from support",
                    Description = "We added more delivery options in your region.",
                    TargetUrl = "/Buyer/Orders/Index",
                    Category = "Messages",
                    CreatedOn = now.AddHours(-18),
                    IsRead = true
                }
            };
        }

        private static IEnumerable<NotificationItem> CreateSellerSeeds(DateTimeOffset now)
        {
            return new[]
            {
                new NotificationItem
                {
                    Id = Guid.NewGuid(),
                    Title = "New order received",
                    Description = "A buyer placed an order in your store. Review it now.",
                    TargetUrl = "/Seller/Orders/Index",
                    Category = "Orders",
                    CreatedOn = now.AddHours(-1),
                    IsRead = false
                },
                new NotificationItem
                {
                    Id = Guid.NewGuid(),
                    Title = "Return case assigned",
                    Description = "A return request needs your response.",
                    TargetUrl = "/Seller/Cases/Index",
                    Category = "Returns",
                    CreatedOn = now.AddHours(-6),
                    IsRead = false
                },
                new NotificationItem
                {
                    Id = Guid.NewGuid(),
                    Title = "Payout initiated",
                    Description = "Your weekly payout is being processed.",
                    TargetUrl = "/Seller/Orders/Index",
                    Category = "Payouts",
                    CreatedOn = now.AddHours(-10),
                    IsRead = true
                }
            };
        }

        private static IEnumerable<NotificationItem> CreateAdminSeeds(DateTimeOffset now)
        {
            return new[]
            {
                new NotificationItem
                {
                    Id = Guid.NewGuid(),
                    Title = "Platform health",
                    Description = "All services are operating normally.",
                    TargetUrl = "/Admin/Dashboard",
                    Category = "System",
                    CreatedOn = now.AddHours(-3),
                    IsRead = false
                },
                new NotificationItem
                {
                    Id = Guid.NewGuid(),
                    Title = "Reviews awaiting action",
                    Description = "There are flagged reviews pending moderation.",
                    TargetUrl = "/Admin/Reviews/Index",
                    Category = "Reviews",
                    CreatedOn = now.AddHours(-7),
                    IsRead = false
                },
                new NotificationItem
                {
                    Id = Guid.NewGuid(),
                    Title = "Security reminder",
                    Description = "Check audit logs to monitor recent admin access.",
                    TargetUrl = "/Admin/Dashboard",
                    Category = "Security",
                    CreatedOn = now.AddHours(-20),
                    IsRead = true
                }
            };
        }

        private static NotificationItem Clone(NotificationItem item) =>
            new()
            {
                Id = item.Id,
                Title = item.Title,
                Description = item.Description,
                TargetUrl = item.TargetUrl,
                Category = item.Category,
                CreatedOn = item.CreatedOn,
                IsRead = item.IsRead
            };
    }
}
