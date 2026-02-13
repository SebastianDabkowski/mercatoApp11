using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SD.ProjectName.WebApp.Services
{
    public class RecentlyViewedService
    {
        private const int MaxAllowedItems = 50;
        private readonly GetProducts _getProducts;
        private readonly RecentlyViewedOptions _options;
        private readonly ILogger<RecentlyViewedService> _logger;

        public RecentlyViewedService(GetProducts getProducts, RecentlyViewedOptions options, ILogger<RecentlyViewedService> logger)
        {
            _getProducts = getProducts;
            _options = options;
            _logger = logger;
        }

        public void RememberProduct(HttpContext context, int productId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (productId <= 0)
            {
                return;
            }

            var ids = ReadIds(context);
            var filtered = ids.Where(id => id != productId).ToList();
            filtered.Insert(0, productId);

            WriteIds(context, filtered);
        }

        public async Task<IReadOnlyList<ProductModel>> GetProductsAsync(HttpContext context, int? excludeProductId = null, CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var ids = ReadIds(context);
            if (excludeProductId.HasValue)
            {
                ids = ids.Where(id => id != excludeProductId.Value).ToList();
            }

            if (ids.Count == 0)
            {
                return Array.Empty<ProductModel>();
            }

            var products = await _getProducts.GetByIds(ids, includeDrafts: false);
            if (products.Count == 0)
            {
                WriteIds(context, Array.Empty<int>());
                return Array.Empty<ProductModel>();
            }

            var productLookup = products.ToDictionary(p => p.Id);
            var ordered = ids.Where(productLookup.ContainsKey).Select(id => productLookup[id]).ToList();

            if (ordered.Count != ids.Count)
            {
                WriteIds(context, ordered.Select(p => p.Id));
            }

            return ordered;
        }

        private List<int> ReadIds(HttpContext context)
        {
            if (!context.Request.Cookies.TryGetValue(_options.CookieName, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return new List<int>();
            }

            var limit = GetLimit();
            var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var ids = new List<int>();

            foreach (var part in parts)
            {
                if (int.TryParse(part, out var id) && id > 0 && !ids.Contains(id))
                {
                    ids.Add(id);
                }

                if (ids.Count >= limit)
                {
                    break;
                }
            }

            return ids;
        }

        private void WriteIds(HttpContext context, IEnumerable<int> ids)
        {
            var normalized = ids.Where(id => id > 0)
                .Distinct()
                .Take(GetLimit())
                .ToList();

            if (normalized.Count == 0)
            {
                context.Response.Cookies.Delete(_options.CookieName, BuildCookieOptions());
                return;
            }

            var value = string.Join(',', normalized);
            context.Response.Cookies.Append(_options.CookieName, value, BuildCookieOptions());
        }

        private CookieOptions BuildCookieOptions()
        {
            return new CookieOptions
            {
                HttpOnly = true,
                IsEssential = false,
                SameSite = SameSiteMode.Lax,
                Secure = true,
                Expires = DateTimeOffset.UtcNow.AddDays(_options.CookieLifespanDays),
                Path = "/"
            };
        }

        private int GetLimit()
        {
            var limit = _options.MaxItems;
            if (limit <= 0)
            {
                _logger.LogWarning("Recently viewed MaxItems misconfigured as {MaxItems}. Falling back to 1.", limit);
                return 1;
            }

            return Math.Min(limit, MaxAllowedItems);
        }
    }
}
