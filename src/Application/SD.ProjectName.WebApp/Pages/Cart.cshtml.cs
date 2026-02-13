using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages
{
    public class CartModel : PageModel
    {
        private readonly CartViewService _cartViewService;

        public List<CartSellerGroup> SellerGroups { get; private set; } = new();
        public decimal ItemsSubtotal { get; private set; }
        public decimal ShippingTotal { get; private set; }
        public decimal GrandTotal { get; private set; }
        public int TotalQuantity { get; private set; }

        public CartModel(CartViewService cartViewService)
        {
            _cartViewService = cartViewService;
        }

        public async Task OnGet()
        {
            var summary = await _cartViewService.BuildAsync(HttpContext);
            SellerGroups = summary.SellerGroups;
            ItemsSubtotal = summary.ItemsSubtotal;
            ShippingTotal = summary.ShippingTotal;
            GrandTotal = summary.GrandTotal;
            TotalQuantity = summary.TotalQuantity;
        }
    }
}
