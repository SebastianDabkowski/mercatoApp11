using SD.ProjectName.Modules.Products.Domain;

namespace SD.ProjectName.WebApp.Services
{
    public record CartSummary(List<CartSellerGroup> SellerGroups, decimal GrandTotal, int TotalQuantity)
    {
        public bool IsEmpty => SellerGroups.Count == 0;

        public static CartSummary Empty { get; } = new(new List<CartSellerGroup>(), 0, 0);
    }

    public record CartSellerGroup(string SellerId, string SellerName, decimal Subtotal, List<CartDisplayItem> Items);

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
