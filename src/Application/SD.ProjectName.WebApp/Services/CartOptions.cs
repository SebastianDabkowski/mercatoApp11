using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Services
{
    public class CartOptions
    {
        public const string SectionName = "Cart";

        [Required]
        [MaxLength(64)]
        public string CookieName { get; set; } = ".SD.Cart";

        [Range(1, 100)]
        public int MaxItems { get; set; } = 50;

        [Range(1, 365)]
        public int CookieLifespanDays { get; set; } = 30;

        public decimal DefaultShippingBase { get; set; } = 0;

        public decimal DefaultShippingPerItem { get; set; } = 0;

        public decimal? DefaultFreeShippingThreshold { get; set; }

        [Range(0, 1)]
        public decimal PlatformCommissionRate { get; set; } = 0.1m;

        [Range(2, 6)]
        public int CommissionPrecision { get; set; } = 4;

        public Dictionary<string, decimal> SellerCommissionOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, decimal> CategoryCommissionRates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<CartShippingRule> ShippingRules { get; set; } = new();
    }

    public class CartShippingRule
    {
        public string SellerId { get; set; } = string.Empty;

        public decimal BaseRate { get; set; }

        public decimal PerItemRate { get; set; }

        public decimal? FreeShippingThreshold { get; set; }
    }
}
