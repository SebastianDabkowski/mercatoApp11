using SD.ProjectName.Modules.Products.Domain;

namespace SD.ProjectName.WebApp.Services
{
    public record CartSummary(
        List<CartSellerGroup> SellerGroups,
        decimal ItemsSubtotal,
        decimal ShippingTotal,
        decimal GrandTotal,
        int TotalQuantity,
        CartSettlementSummary Settlement,
        decimal DiscountTotal = 0,
        string? AppliedPromoCode = null)
    {
        public bool IsEmpty => SellerGroups.Count == 0;

        public static CartSummary Empty { get; } = new(new List<CartSellerGroup>(), 0, 0, 0, 0, CartSettlementSummary.Empty, 0, null);
    }

    public record CartSettlementSummary(List<CartSellerSettlement> Sellers, decimal PlatformCommissionTotal, decimal SellerPayoutTotal)
    {
        public static CartSettlementSummary Empty { get; } = new(new List<CartSellerSettlement>(), 0, 0);
    }

    public record CartSellerSettlement(string SellerId, decimal Subtotal, decimal Shipping, decimal Commission, decimal Payout);

    public record CartSellerGroup(string SellerId, string SellerName, decimal Subtotal, decimal Shipping, decimal Total, List<CartDisplayItem> Items);

    public record CartDisplayItem(
        ProductModel Product,
        int Quantity,
        string VariantLabel,
        decimal UnitPrice,
        decimal LineTotal,
        bool IsAvailable,
        int AvailableStock,
        IReadOnlyDictionary<string, string> VariantAttributes);
}
