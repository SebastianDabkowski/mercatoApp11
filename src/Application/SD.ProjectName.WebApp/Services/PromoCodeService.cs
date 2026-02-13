using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SD.ProjectName.WebApp.Services
{
    public class PromoOptions
    {
        public const string SectionName = "Promo";

        [Required]
        [MaxLength(64)]
        public string CookieName { get; set; } = ".SD.Promo";

        [Range(1, 90)]
        public int CookieLifespanDays { get; set; } = 14;

        public bool AllowMultipleCodes { get; set; }

        public List<PromoCodeRule> Codes { get; set; } = new()
        {
            new PromoCodeRule
            {
                Code = "SAVE10",
                Description = "10% off your items",
                DiscountType = PromoDiscountType.Percentage,
                Value = 0.10m
            }
        };
    }

    public class PromoCodeRule
    {
        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Range(0, double.MaxValue)]
        public decimal Value { get; set; }

        public PromoDiscountType DiscountType { get; set; } = PromoDiscountType.Percentage;

        [MaxLength(64)]
        public string? SellerId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? MinimumSubtotal { get; set; }

        public DateTimeOffset? ExpiresOn { get; set; }

        public bool Active { get; set; } = true;

        [MaxLength(200)]
        public string? Description { get; set; }
    }

    public enum PromoDiscountType
    {
        Percentage = 0,
        FixedAmount = 1
    }

    public record PromoApplicationResult(bool Success, string Message, CartSummary Summary, string? AppliedCode, bool AlreadyApplied);

    public class PromoCodeService
    {
        private readonly PromoOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PromoCodeService> _logger;

        public PromoCodeService(PromoOptions options, TimeProvider timeProvider, ILogger<PromoCodeService> logger)
        {
            _options = options;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public PromoApplicationResult ApplyStored(HttpContext context, CartSummary summary)
        {
            var stored = GetStoredCode(context);
            if (string.IsNullOrWhiteSpace(stored))
            {
                return BuildResult(false, string.Empty, ResetDiscount(summary), null, false);
            }

            var evaluation = Evaluate(stored, summary);
            if (!evaluation.Success)
            {
                Clear(context);
                return BuildResult(false, evaluation.Message, ResetDiscount(summary), null, false);
            }

            var updated = ApplyDiscount(summary, evaluation);
            return BuildResult(true, evaluation.Message, updated, evaluation.Code, false);
        }

        public PromoApplicationResult Apply(HttpContext context, CartSummary summary, string code)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var normalized = Normalize(code);
            var stored = GetStoredCode(context);
            if (!string.IsNullOrWhiteSpace(stored)
                && !string.Equals(stored, normalized, StringComparison.OrdinalIgnoreCase)
                && !_options.AllowMultipleCodes)
            {
                var current = ApplyStored(context, summary);
                var message = "Only one promo code can be applied. Remove the current code to try another.";
                return BuildResult(false, message, current.Summary, stored, true);
            }

            var evaluation = Evaluate(normalized, summary);
            if (!evaluation.Success)
            {
                Clear(context);
                return BuildResult(false, evaluation.Message, ResetDiscount(summary), null, false);
            }

            Save(context, evaluation.Code);
            var updatedSummary = ApplyDiscount(summary, evaluation);
            var successMessage = string.IsNullOrWhiteSpace(evaluation.Message) ? "Promo code applied." : evaluation.Message;
            return BuildResult(true, successMessage, updatedSummary, evaluation.Code, false);
        }

        public void Clear(HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.Response.Cookies.Delete(_options.CookieName, BuildCookieOptions());
        }

        internal string? GetStoredCode(HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (!context.Request.Cookies.TryGetValue(_options.CookieName, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return Normalize(value);
        }

        private PromoApplicationResult BuildResult(bool success, string message, CartSummary summary, string? appliedCode, bool alreadyApplied)
        {
            return new PromoApplicationResult(success, message, summary, appliedCode, alreadyApplied);
        }

        private PromoEvaluationResult Evaluate(string? code, CartSummary summary)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return PromoEvaluationResult.Failed("Enter a promo code.");
            }

            var normalized = Normalize(code);
            var rule = _options.Codes.FirstOrDefault(r => string.Equals(r.Code, normalized, StringComparison.OrdinalIgnoreCase) && r.Active);
            if (rule == null)
            {
                return PromoEvaluationResult.Failed("Promo code is invalid.");
            }

            if (rule.ExpiresOn.HasValue && rule.ExpiresOn.Value < _timeProvider.GetUtcNow())
            {
                return PromoEvaluationResult.Failed("This promo code has expired.");
            }

            var targetSubtotal = ResolveTargetSubtotal(summary, rule.SellerId);
            if (targetSubtotal <= 0)
            {
                return PromoEvaluationResult.Failed("This promo code does not apply to your items.");
            }

            if (rule.MinimumSubtotal.HasValue && targetSubtotal < rule.MinimumSubtotal.Value)
            {
                return PromoEvaluationResult.Failed("Order does not meet the promo requirements.");
            }

            var discount = CalculateDiscount(targetSubtotal, rule);
            if (discount <= 0)
            {
                return PromoEvaluationResult.Failed("Promo code is not applicable.");
            }

            return PromoEvaluationResult.Successful(normalized, discount, rule.SellerId, rule.Description);
        }

        private static decimal ResolveTargetSubtotal(CartSummary summary, string? sellerId)
        {
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return summary.ItemsSubtotal;
            }

            return summary.SellerGroups
                .Where(g => string.Equals(g.SellerId, sellerId, StringComparison.OrdinalIgnoreCase))
                .Sum(g => g.Subtotal);
        }

        private static decimal CalculateDiscount(decimal baseAmount, PromoCodeRule rule)
        {
            var normalizedBase = Math.Max(0, baseAmount);
            decimal discount;
            if (rule.DiscountType == PromoDiscountType.FixedAmount)
            {
                discount = rule.Value;
            }
            else
            {
                discount = normalizedBase * rule.Value;
            }

            discount = Math.Min(discount, normalizedBase);
            return Math.Round(discount, 2, MidpointRounding.AwayFromZero);
        }

        private static CartSummary ApplyDiscount(CartSummary summary, PromoEvaluationResult evaluation)
        {
            var discount = Math.Min(evaluation.DiscountAmount, summary.ItemsSubtotal + summary.ShippingTotal);
            var roundedDiscount = Math.Round(discount, 2, MidpointRounding.AwayFromZero);
            var grandTotal = Math.Max(0, summary.ItemsSubtotal + summary.ShippingTotal - roundedDiscount);
            return summary with { DiscountTotal = roundedDiscount, AppliedPromoCode = evaluation.Code, GrandTotal = grandTotal };
        }

        private static CartSummary ResetDiscount(CartSummary summary)
        {
            var grandTotal = summary.ItemsSubtotal + summary.ShippingTotal;
            return summary with { DiscountTotal = 0, AppliedPromoCode = null, GrandTotal = grandTotal };
        }

        private void Save(HttpContext context, string code)
        {
            try
            {
                var options = BuildCookieOptions();
                context.Response.Cookies.Append(_options.CookieName, Normalize(code), options);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist promo code.");
            }
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

        private static string Normalize(string value)
        {
            return value?.Trim().ToUpperInvariant() ?? string.Empty;
        }

        private record PromoEvaluationResult(bool Success, string Message, string Code, decimal DiscountAmount, string? SellerId, string? Description)
        {
            public static PromoEvaluationResult Failed(string message) => new(false, message, string.Empty, 0, null, null);

            public static PromoEvaluationResult Successful(string code, decimal discountAmount, string? sellerId, string? description) =>
                new(true, description ?? string.Empty, code, discountAmount, sellerId, description);
        }
    }
}
