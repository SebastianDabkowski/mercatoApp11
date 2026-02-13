using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products
{
    public class OrderServiceTests
    {
        private static readonly DeliveryAddress TestAddress = new("Buyer One", "1 Market St", null, "Metropolis", "NY", "10001", "US", "555-1111");

        [Fact]
        public async Task EnsureOrderAsync_ShouldCreateOrder_AndSendEmail()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-1", "sig-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-1", "buyer@example.com", "Buyer One", "Card", "card");

            Assert.True(result.Created);
            Assert.Equal(1, context.Orders.Count());
            Assert.Equal(quote.Summary.GrandTotal, result.Order.GrandTotal);
            Assert.Equal(quote.Summary.TotalQuantity, result.Order.TotalQuantity);
            Assert.Equal(OrderStatuses.Paid, result.Order.Status);

            var view = await service.GetOrderAsync(result.Order.Id, "buyer-1");
            Assert.NotNull(view);
            Assert.Equal("Card", view!.PaymentMethodLabel);
            Assert.Single(view.Items);
            Assert.Single(view.SubOrders);
            Assert.Equal(view.GrandTotal, view.SubOrders.First().GrandTotal);
            Assert.Equal(view.SubOrders.First().TotalQuantity, view.TotalQuantity);
            Assert.Equal(OrderStatuses.Paid, view.Status);
            Assert.Equal(PaymentStatuses.Paid, view.PaymentStatus);
            Assert.All(view.SubOrders, s => Assert.Equal(OrderStatuses.Paid, s.Status));

            emailSender.Verify(e => e.SendEmailAsync("buyer@example.com", It.Is<string>(s => s.Contains(result.Order.OrderNumber)), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task EnsureOrderAsync_ShouldCreateFailedOrder_WhenPaymentFails()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "blik", CheckoutPaymentStatus.Failed, "ref-failed-1", "sig-failed-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-failed", "buyerfailed@example.com", "Buyer Failed", "BLIK", "blik", OrderStatuses.Failed);

            Assert.True(result.Created);
            Assert.Equal(OrderStatuses.Failed, result.Order.Status);
            var view = await service.GetOrderAsync(result.Order.Id, "buyer-failed");
            Assert.NotNull(view);
            Assert.Equal(OrderStatuses.Failed, view!.Status);
            Assert.Equal(PaymentStatuses.Failed, view.PaymentStatus);
            Assert.False(string.IsNullOrWhiteSpace(view.PaymentStatusMessage));
            Assert.All(view.SubOrders, s => Assert.Equal(OrderStatuses.Failed, s.Status));
            emailSender.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task EnsureOrderAsync_ShouldBeIdempotent_ForPaymentReference()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-2", "sig-2");

            var first = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-2", "buyer2@example.com", "Buyer Two", "Card", "card");
            var second = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-2", "buyer2@example.com", "Buyer Two", "Card", "card");

            Assert.True(first.Created);
            Assert.False(second.Created);
            Assert.Equal(first.Order.Id, second.Order.Id);
            Assert.Equal(1, context.Orders.Count());
            emailSender.Verify(e => e.SendEmailAsync("buyer2@example.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task EnsureOrderAsync_ShouldSplitIntoSellerSubOrders()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildMultiSellerQuote();
            var selections = new Dictionary<string, string> { ["seller-1"] = "express", ["seller-2"] = "standard" };
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, selections, "card", CheckoutPaymentStatus.Confirmed, "ref-3", "sig-3");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-3", "buyer3@example.com", "Buyer Three", "Card", "card");

            Assert.True(result.Created);
            var view = await service.GetOrderAsync(result.Order.Id, "buyer-3");
            Assert.NotNull(view);
            Assert.Equal(2, view!.SubOrders.Count);
            Assert.Equal(view.SubOrders.Sum(s => s.GrandTotal), view.GrandTotal);
            var firstSeller = view.SubOrders.First(s => s.SellerId == "seller-1");
            Assert.Equal("express", firstSeller.ShippingDetail.MethodId);
            Assert.Equal(7, firstSeller.Shipping);
            Assert.True(firstSeller.Items.All(i => i.SellerId == "seller-1"));
            Assert.Equal(OrderStatuses.Paid, firstSeller.Status);
            var secondSeller = view.SubOrders.First(s => s.SellerId == "seller-2");
            Assert.Equal("standard", secondSeller.ShippingDetail.MethodId);
            Assert.Equal(5, secondSeller.Shipping);
            Assert.True(secondSeller.Items.All(i => i.SellerId == "seller-2"));
            Assert.Equal(OrderStatuses.Paid, secondSeller.Status);
        }

        [Fact]
        public async Task EnsureOrderAsync_ShouldStoreShippingEstimate()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);

            var product = new ProductModel { Id = 3, Title = "Courier Item", Price = 15, Stock = 2, SellerId = "seller-1" };
            var displayItem = new CartDisplayItem(product, 1, "Default", 15, 15, true, 2, new Dictionary<string, string>());
            var sellerGroup = new CartSellerGroup("seller-1", "Seller One", 15, 6, 21, new List<CartDisplayItem> { displayItem });
            var summary = new CartSummary(new List<CartSellerGroup> { sellerGroup }, 15, 6, 21, 1, CartSettlementSummary.Empty);
            var shippingOption = new ShippingMethodOption("courier", "Courier", 6, "Tracked courier", true, "2-3 business days");
            var sellerOptions = new List<SellerShippingOptions> { new("seller-1", "Seller One", new List<ShippingMethodOption> { shippingOption }) };
            var selections = new Dictionary<string, string> { ["seller-1"] = "courier" };
            var quote = new ShippingQuote(summary, sellerOptions, selections);
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, selections, "card", CheckoutPaymentStatus.Confirmed, "ref-ship-quote", "sig-ship-quote");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-ship", "ship@example.com", "Buyer Ship", "Card", "card");

            Assert.True(result.Created);
            var view = await service.GetOrderAsync(result.Order.Id, "buyer-ship");
            Assert.NotNull(view);
            var shipping = Assert.Single(view!.SubOrders).ShippingDetail;
            Assert.Equal("courier", shipping.MethodId);
            Assert.Equal("Courier", shipping.MethodLabel);
            Assert.Equal(6, shipping.Cost);
            Assert.Equal("2-3 business days", shipping.DeliveryEstimate);
            Assert.Equal("Tracked courier", shipping.Description);
        }

        [Fact]
        public async Task EnsureOrderAsync_ShouldStoreEscrowPerSeller()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildMultiSellerQuote();
            var selections = new Dictionary<string, string> { ["seller-1"] = "express", ["seller-2"] = "standard" };
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, selections, "card", CheckoutPaymentStatus.Confirmed, "ref-escrow-1", "sig-escrow-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-escrow", "escrow@example.com", "Escrow Buyer", "Card", "card");
            var view = await service.GetOrderAsync(result.Order.Id, "buyer-escrow");

            Assert.NotNull(view);
            Assert.Equal(2, view!.Escrow.Count);
            foreach (var subOrder in view.SubOrders)
            {
                var allocation = view.Escrow.FirstOrDefault(e => e.SubOrderNumber == subOrder.SubOrderNumber);
                Assert.NotNull(allocation);
                Assert.Equal(subOrder.GrandTotal, allocation!.HeldAmount);
                var expectedCommission = subOrder.SellerId == "seller-1" ? 1 : 4;
                var expectedPayout = subOrder.SellerId == "seller-1" ? 16 : 41;
                Assert.Equal(expectedCommission, allocation.CommissionAmount);
                Assert.Equal(expectedPayout, allocation.SellerPayoutAmount);
                Assert.Contains(allocation.Ledger, l => l.Type == EscrowEntryTypes.Hold && l.Amount == allocation.HeldAmount);
            }
        }

        [Fact]
        public async Task GetSellerOrderAsync_ShouldReturnOnlySellerItems()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildMultiSellerQuote();
            var selections = new Dictionary<string, string> { ["seller-1"] = "express", ["seller-2"] = "standard" };
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, selections, "card", CheckoutPaymentStatus.Confirmed, "ref-4", "sig-4");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-4", "buyer4@example.com", "Buyer Four", "Card", "card");
            var sellerOrder = await service.GetSellerOrderAsync(result.Order.Id, "seller-2");

            Assert.NotNull(sellerOrder);
            Assert.Equal("seller-2", sellerOrder!.Shipping.SellerId);
            Assert.True(sellerOrder.Items.All(i => i.SellerId == "seller-2"));
            Assert.Equal(sellerOrder.ShippingTotal + sellerOrder.ItemsSubtotal - sellerOrder.DiscountTotal, sellerOrder.GrandTotal);
            Assert.Equal(OrderStatuses.Paid, sellerOrder.Status);
            Assert.Equal("Buyer Four", sellerOrder.BuyerName);
            Assert.Equal("buyer4@example.com", sellerOrder.BuyerEmail);
            Assert.Equal(TestAddress.Phone, sellerOrder.BuyerPhone);
            Assert.Equal(PaymentStatuses.Paid, sellerOrder.PaymentStatus);

            var sellerSummaries = await service.GetSummariesForSellerAsync("seller-2");
            Assert.Equal(1, sellerSummaries.TotalCount);
            var summary = Assert.Single(sellerSummaries.Items);
            Assert.Equal(result.Order.Id, summary.Id);
            Assert.Equal(OrderStatuses.Paid, summary.Status);
        }

        [Fact]
        public async Task SubmitSellerRatingAsync_ShouldReject_WhenOrderNotDelivered()
        {
            await using var context = CreateContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-seller-rating-1", "sig-seller-rating-1");

            var creation = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-seller-rating", "buyer-rating@example.com", "Buyer Rating", "Card", "card");
            var result = await service.SubmitSellerRatingAsync(creation.Order.Id, "seller-1", "buyer-seller-rating", 5);

            Assert.False(result.Success);
            Assert.Empty(context.SellerRatings);
        }

        [Fact]
        public async Task SubmitSellerRatingAsync_ShouldStoreRating_WhenDelivered()
        {
            await using var context = CreateContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-seller-rating-2", "sig-seller-rating-2");

            var creation = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-seller-rating-2", "buyer-rating2@example.com", "Buyer Rating 2", "Card", "card");
            await service.UpdateSubOrderStatusAsync(creation.Order.Id, "seller-1", OrderStatuses.Delivered);

            var result = await service.SubmitSellerRatingAsync(creation.Order.Id, "seller-1", "buyer-seller-rating-2", 4);

            Assert.True(result.Success);
            Assert.NotNull(result.Rating);
            Assert.Equal(4, result.Rating!.Rating);
            Assert.Equal("seller-1", result.Rating.SellerId);
            Assert.Equal(1, context.SellerRatings.Count());
            var average = await service.GetSellerRatingScoreAsync("seller-1");
            Assert.Equal(4, average);
        }

        [Fact]
        public async Task SubmitSellerRatingAsync_ShouldPreventDuplicatePerOrder()
        {
            await using var context = CreateContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-seller-rating-3", "sig-seller-rating-3");

            var creation = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-seller-rating-3", "buyer-rating3@example.com", "Buyer Rating 3", "Card", "card");
            await service.UpdateSubOrderStatusAsync(creation.Order.Id, "seller-1", OrderStatuses.Delivered);

            var first = await service.SubmitSellerRatingAsync(creation.Order.Id, "seller-1", "buyer-seller-rating-3", 5);
            var second = await service.SubmitSellerRatingAsync(creation.Order.Id, "seller-1", "buyer-seller-rating-3", 3);

            Assert.True(first.Success);
            Assert.False(second.Success);
            Assert.Equal(1, context.SellerRatings.Count());
        }

        [Fact]
        public async Task GetSellerRatingSummaryAsync_ShouldReturnAverageAndCount()
        {
            await using var context = CreateContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);
            context.SellerRatings.AddRange(
                new SellerRating { OrderId = 1, SellerId = "seller-avg", BuyerId = "buyer-1", Rating = 4, CreatedOn = DateTimeOffset.UtcNow },
                new SellerRating { OrderId = 2, SellerId = "seller-avg", BuyerId = "buyer-2", Rating = 5, CreatedOn = DateTimeOffset.UtcNow });
            await context.SaveChangesAsync();

            var summary = await service.GetSellerRatingSummaryAsync("seller-avg");

            Assert.Equal(2, summary.RatedOrderCount);
            Assert.Equal(4.5, summary.AverageRating);

            var missing = await service.GetSellerRatingSummaryAsync("seller-none");
            Assert.Equal(0, missing.RatedOrderCount);
            Assert.Null(missing.AverageRating);
        }

        [Fact]
        public async Task SubmitProductReviewAsync_ShouldReject_WhenOrderNotDelivered()
        {
            await using var context = CreateContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-review-1", "sig-review-1");

            var creation = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-review", "buyer-review@example.com", "Buyer Review", "Card", "card");
            var result = await service.SubmitProductReviewAsync(creation.Order.Id, 1, "buyer-review", "Buyer Review", 5, "Great item");

            Assert.False(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
            Assert.Empty(context.ProductReviews);
        }

        [Fact]
        public async Task SubmitProductReviewAsync_ShouldCreateReview_WhenDelivered()
        {
            await using var context = CreateContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-review-2", "sig-review-2");

            var creation = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-review-2", "buyer2@example.com", "Buyer Reviewer", "Card", "card");
            await service.UpdateSubOrderStatusAsync(creation.Order.Id, "seller-1", OrderStatuses.Delivered);

            var result = await service.SubmitProductReviewAsync(creation.Order.Id, 1, "buyer-review-2", "Buyer Reviewer", 4, "Solid quality");

            Assert.True(result.Success);
            Assert.NotNull(result.Review);
            Assert.Equal(4, result.Review!.Rating);
            Assert.Equal(1, context.ProductReviews.Count());
            var stored = await context.ProductReviews.FirstAsync();
            Assert.Equal("buyer-review-2", stored.BuyerId);
            Assert.Equal(ReviewStatuses.Published, stored.Status);
        }

        [Fact]
        public async Task SubmitProductReviewAsync_ShouldRateLimitRapidSubmissions()
        {
            await using var context = CreateContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);
            var quote = BuildMultiItemQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-review-3", "sig-review-3");

            var creation = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-review-3", "buyer3@example.com", "Buyer Three", "Card", "card");
            await service.UpdateSubOrderStatusAsync(creation.Order.Id, "seller-1", OrderStatuses.Delivered);

            var first = await service.SubmitProductReviewAsync(creation.Order.Id, 1, "buyer-review-3", "Buyer Three", 5, "Excellent");
            var second = await service.SubmitProductReviewAsync(creation.Order.Id, 2, "buyer-review-3", "Buyer Three", 4, "Also good");

            Assert.True(first.Success);
            Assert.False(second.Success);
            Assert.Equal(1, context.ProductReviews.Count());
        }

        [Fact]
        public async Task SubmitProductReviewAsync_ShouldFlagWhenContainsBannedTerms()
        {
            await using var context = CreateContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-review-flag", "sig-review-flag");

            var creation = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-flag", "flag@example.com", "Flag Buyer", "Card", "card");
            await service.UpdateSubOrderStatusAsync(creation.Order.Id, "seller-1", OrderStatuses.Delivered);

            var result = await service.SubmitProductReviewAsync(creation.Order.Id, 1, "buyer-flag", "Flag Buyer", 2, "This looks like spam http://spam.test");

            Assert.True(result.Success);
            var stored = await context.ProductReviews.FirstAsync();
            Assert.True(stored.IsFlagged);
            Assert.Equal(ReviewStatuses.Pending, stored.Status);
            Assert.False(string.IsNullOrWhiteSpace(stored.FlagReason));
            var audit = await context.ProductReviewAudits.FirstOrDefaultAsync();
            Assert.NotNull(audit);
            Assert.Equal("Flagged", audit!.Action);
            Assert.Equal(stored.Id, audit.ReviewId);
        }

        [Fact]
        public async Task ApproveReviewAsync_ShouldPublishAndClearFlag()
        {
            await using var context = CreateContext();
            var review = new ProductReview
            {
                ProductId = 3,
                OrderId = 2,
                BuyerId = "buyer-audit",
                BuyerName = "Buyer Audit",
                Rating = 4,
                Comment = "Pending review",
                CreatedOn = DateTimeOffset.UtcNow.AddMinutes(-10),
                Status = ReviewStatuses.Pending,
                IsFlagged = true,
                FlagReason = "Contains link"
            };
            context.ProductReviews.Add(review);
            await context.SaveChangesAsync();

            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);
            var result = await service.ApproveReviewAsync(review.Id, "moderator@example.com", "Clean");

            Assert.True(result.Success);
            var updated = await context.ProductReviews.FirstAsync();
            Assert.Equal(ReviewStatuses.Published, updated.Status);
            Assert.False(updated.IsFlagged);
            Assert.Equal("moderator@example.com", updated.LastModeratedBy);
            Assert.NotNull(updated.LastModeratedOn);
            var audit = await context.ProductReviewAudits.FirstOrDefaultAsync(a => a.ReviewId == updated.Id && a.Action == "Approved");
            Assert.NotNull(audit);
            Assert.Equal(ReviewStatuses.Published, audit!.ToStatus);
        }

        [Fact]
        public async Task RejectReviewAsync_ShouldRemoveFromPublishedFeed()
        {
            await using var context = CreateContext();
            var review = new ProductReview
            {
                ProductId = 5,
                OrderId = 9,
                BuyerId = "buyer-reject",
                BuyerName = "Buyer Reject",
                Rating = 5,
                Comment = "Great",
                CreatedOn = DateTimeOffset.UtcNow,
                Status = ReviewStatuses.Published
            };
            context.ProductReviews.Add(review);
            await context.SaveChangesAsync();

            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);

            var reject = await service.RejectReviewAsync(review.Id, "admin", "Inappropriate");
            Assert.True(reject.Success);

            var page = await service.GetPublishedReviewsPageAsync(5);
            Assert.Empty(page.Reviews);
            var stored = await context.ProductReviews.FirstAsync();
            Assert.Equal(ReviewStatuses.Rejected, stored.Status);
            Assert.True(stored.IsFlagged);
        }

        [Fact]
        public async Task GetPublishedReviewsPageAsync_ShouldReturnSortedPageWithAverage()
        {
            await using var context = CreateContext();
            var service = new OrderService(context, Mock.Of<IEmailSender>(), NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;
            context.ProductReviews.AddRange(
                new ProductReview { ProductId = 7, OrderId = 1, BuyerId = "buyer-1", BuyerName = "Buyer One", Rating = 3, Comment = "Ok", CreatedOn = now.AddDays(-1), Status = ReviewStatuses.Published },
                new ProductReview { ProductId = 7, OrderId = 1, BuyerId = "buyer-2", BuyerName = "Buyer Two", Rating = 5, Comment = "Great", CreatedOn = now, Status = ReviewStatuses.Published },
                new ProductReview { ProductId = 7, OrderId = 1, BuyerId = "buyer-3", BuyerName = "Buyer Three", Rating = 1, Comment = "Bad", CreatedOn = now.AddDays(-2), Status = ReviewStatuses.Published },
                new ProductReview { ProductId = 7, OrderId = 1, BuyerId = "buyer-4", BuyerName = "Buyer Four", Rating = 4, Comment = "Hidden", CreatedOn = now, Status = ReviewStatuses.Pending });
            await context.SaveChangesAsync();

            var page = await service.GetPublishedReviewsPageAsync(7, page: 2, pageSize: 1, sort: "highest");

            Assert.Equal(3, page.TotalCount);
            Assert.Equal(2, page.PageNumber);
            Assert.Equal("highest", page.Sort);
            Assert.NotNull(page.AverageRating);
            Assert.Equal(3.0, page.AverageRating!.Value);
            var review = Assert.Single(page.Reviews);
            Assert.Equal(3, review.Rating);
            Assert.Equal("Buyer One", review.BuyerName);
        }

        [Fact]
        public async Task UpdatePaymentStatusAsync_ShouldApplyWebhookStatus()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-webhook-1", "sig-webhook-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-webhook", "webhook@example.com", "Webhook Buyer", "Card", "card");
            var update = await service.UpdatePaymentStatusAsync("ref-webhook-1", "refunded", 5m);

            Assert.True(update.Success);
            Assert.Equal(PaymentStatuses.Refunded, update.PaymentStatus);
            Assert.Equal(5m, update.PaymentRefundedAmount);

            var view = await service.GetOrderAsync(result.Order.Id, "buyer-webhook");
            Assert.NotNull(view);
            Assert.Equal(PaymentStatuses.Refunded, view!.PaymentStatus);
            Assert.Equal(5m, view.PaymentRefundedAmount);

            var sellerOrder = await service.GetSellerOrderAsync(result.Order.Id, "seller-1");
            Assert.NotNull(sellerOrder);
            Assert.Equal(PaymentStatuses.Refunded, sellerOrder!.PaymentStatus);
        }

        [Fact]
        public async Task UpdatePaymentStatusAsync_ShouldAdjustEscrowForPartialRefunds()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-webhook-partial", "sig-webhook-partial");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-webhook-partial", "partial@example.com", "Partial Webhook Buyer", "Card", "card");
            var update = await service.UpdatePaymentStatusAsync("ref-webhook-partial", "partial_refund", 10m, "Provider refunded 10");

            Assert.True(update.Success);
            Assert.Equal(PaymentStatuses.Refunded, update.PaymentStatus);
            Assert.Equal(10m, update.PaymentRefundedAmount);

            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-webhook-partial");
            Assert.NotNull(orderView);
            Assert.Equal(10m, orderView!.PaymentRefundedAmount);
            Assert.Equal(PaymentStatuses.Refunded, orderView.PaymentStatus);

            var sellerOrder = await service.GetSellerOrderAsync(result.Order.Id, "seller-1");
            Assert.NotNull(sellerOrder);
            Assert.Equal(10m, sellerOrder!.RefundedAmount);
            Assert.Equal("Provider refunded 10", sellerOrder.PaymentStatusMessage);
            Assert.NotNull(sellerOrder.Escrow);
            Assert.Equal(10m, sellerOrder.Escrow!.ReleasedToBuyer);
            Assert.Equal(1.5m, sellerOrder.Escrow.CommissionAmount);
            Assert.Equal(13.5m, sellerOrder.Escrow.SellerPayoutAmount);
        }

        [Fact]
        public async Task UpdatePaymentStatusAsync_ShouldHandleFullRefunds()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-webhook-full", "sig-webhook-full");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-webhook-full", "full@example.com", "Full Webhook Buyer", "Card", "card");
            var update = await service.UpdatePaymentStatusAsync("ref-webhook-full", "refunded", 25m, "Provider refunded everything");

            Assert.True(update.Success);
            Assert.Equal(PaymentStatuses.Refunded, update.PaymentStatus);
            Assert.Equal(25m, update.PaymentRefundedAmount);

            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-webhook-full");
            Assert.NotNull(orderView);
            Assert.Equal(OrderStatuses.Refunded, orderView!.Status);
            Assert.Equal(25m, orderView.PaymentRefundedAmount);

            var sellerOrder = await service.GetSellerOrderAsync(result.Order.Id, "seller-1");
            Assert.NotNull(sellerOrder);
            Assert.Equal(OrderStatuses.Refunded, sellerOrder!.Status);
            Assert.Equal(25m, sellerOrder.RefundedAmount);
            Assert.Equal("Provider refunded everything", sellerOrder.PaymentStatusMessage);
            Assert.NotNull(sellerOrder.Escrow);
            Assert.Equal(25m, sellerOrder.Escrow!.ReleasedToBuyer);
            Assert.Equal(0m, sellerOrder.Escrow.CommissionAmount);
            Assert.Equal(0m, sellerOrder.Escrow.SellerPayoutAmount);
            Assert.False(sellerOrder.Escrow.PayoutEligible);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldEnforceTransitions_AndPersist()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-10", "sig-10");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-10", "buyer10@example.com", "Buyer Ten", "Card", "card");
            Assert.Equal(OrderStatuses.Paid, result.Order.Status);

            var preparing = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Preparing, null, null);
            Assert.True(preparing.Success);
            Assert.Equal(OrderStatuses.Preparing, preparing.UpdatedSubOrder!.Status);
            Assert.Equal(OrderStatuses.Preparing, preparing.OrderStatus);

            var shipped = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Shipped, "TRACK123", null, "Carrier A");
            Assert.True(shipped.Success);
            Assert.Equal("TRACK123", shipped.UpdatedSubOrder!.TrackingNumber);
            Assert.Equal("Carrier A", shipped.UpdatedSubOrder!.TrackingCarrier);
            Assert.Equal(OrderStatuses.Shipped, shipped.OrderStatus);

            var updatedTracking = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Shipped, "TRACK456", null, "Carrier B");
            Assert.True(updatedTracking.Success);
            Assert.Equal(OrderStatuses.Shipped, updatedTracking.OrderStatus);
            Assert.Equal("TRACK456", updatedTracking.UpdatedSubOrder!.TrackingNumber);
            Assert.Equal("Carrier B", updatedTracking.UpdatedSubOrder!.TrackingCarrier);

            var delivered = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered, "TRACK456", null);
            Assert.True(delivered.Success);
            Assert.Equal(OrderStatuses.Delivered, delivered.UpdatedSubOrder!.Status);
            Assert.Equal(OrderStatuses.Delivered, delivered.OrderStatus);

            var sellerOrder = await service.GetSellerOrderAsync(result.Order.Id, "seller-1");
            Assert.NotNull(sellerOrder);
            Assert.Equal(OrderStatuses.Delivered, sellerOrder!.Status);
            Assert.Equal("TRACK456", sellerOrder.TrackingNumber);
            Assert.Equal("Carrier B", sellerOrder.TrackingCarrier);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldCreateShipment_ForIntegratedProvider()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var providerOptions = new ShippingProviderOptions
            {
                Providers = new List<ShippingProviderDefinition>
                {
                    new()
                    {
                        Id = "shipfast",
                        Name = "ShipFast",
                        Services = new List<ShippingProviderServiceDefinition>
                        {
                            new() { Code = "standard", Name = "ShipFast Standard", TrackingUrlTemplate = "https://track.shipfast.test/{tracking}" }
                        }
                    }
                }
            };
            var shippingProviderService = new ShippingProviderService(providerOptions, TimeProvider.System, NullLogger<ShippingProviderService>.Instance);
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, shippingProviderService: shippingProviderService);
            var quote = BuildQuote("shipfast", "standard");
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-provider-1", "sig-provider-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-provider", "buyerprovider@example.com", "Buyer Provider", "Card", "card");
            var shipped = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Shipped);

            Assert.True(shipped.Success);
            Assert.NotNull(shipped.UpdatedSubOrder);
            Assert.False(string.IsNullOrWhiteSpace(shipped.UpdatedSubOrder!.TrackingNumber));
            Assert.Equal("shipfast", shipped.UpdatedSubOrder.ShippingProviderId);
            Assert.Equal("standard", shipped.UpdatedSubOrder.ShippingProviderService);
            Assert.False(string.IsNullOrWhiteSpace(shipped.UpdatedSubOrder.ShippingProviderReference));
            Assert.Equal("ShipFast", shipped.UpdatedSubOrder.TrackingCarrier);
            Assert.False(string.IsNullOrWhiteSpace(shipped.UpdatedSubOrder.TrackingUrl));
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldStoreShippingLabel_ForProviderShipment()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var providerOptions = new ShippingProviderOptions
            {
                Providers = new List<ShippingProviderDefinition>
                {
                    new()
                    {
                        Id = "shipfast",
                        Name = "ShipFast",
                        Services = new List<ShippingProviderServiceDefinition>
                        {
                            new() { Code = "standard", Name = "ShipFast Standard", TrackingUrlTemplate = "https://track.shipfast.test/{tracking}", LabelRetentionDays = 10 }
                        }
                    }
                }
            };
            var shippingProviderService = new ShippingProviderService(providerOptions, TimeProvider.System, NullLogger<ShippingProviderService>.Instance);
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, shippingProviderService: shippingProviderService);
            var quote = BuildQuote("shipfast", "standard");
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-provider-label", "sig-provider-label");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-label", "buyerlabel@example.com", "Buyer Label", "Card", "card");
            var shipped = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Shipped);

            Assert.True(shipped.Success);
            Assert.NotNull(shipped.UpdatedSubOrder);
            Assert.NotNull(shipped.UpdatedSubOrder!.ShippingLabel);
            Assert.False(string.IsNullOrWhiteSpace(shipped.UpdatedSubOrder!.ShippingLabel!.Base64Content));

            var sellerOrder = await service.GetSellerOrderAsync(result.Order.Id, "seller-1");
            Assert.NotNull(sellerOrder);
            Assert.True(sellerOrder!.HasShippingLabel);
            Assert.NotNull(sellerOrder.ShippingLabelExpiresOn);

            var label = await service.GetShippingLabelAsync(result.Order.Id, "seller-1");
            Assert.NotNull(label);
            Assert.Equal("application/pdf", label!.ContentType);
            Assert.NotEmpty(label.Content);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldNotShip_WhenLabelGenerationFails()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var providerOptions = new ShippingProviderOptions
            {
                Providers = new List<ShippingProviderDefinition>
                {
                    new()
                    {
                        Id = "shipfast",
                        Name = "ShipFast",
                        Services = new List<ShippingProviderServiceDefinition>
                        {
                            new() { Code = "standard", Name = "ShipFast Standard", TrackingUrlTemplate = "https://track.shipfast.test/{tracking}" }
                        }
                    }
                }
            };
            var shippingProviderService = new FailingLabelShippingProviderService(providerOptions, TimeProvider.System, NullLogger<ShippingProviderService>.Instance);
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, shippingProviderService: shippingProviderService);
            var quote = BuildQuote("shipfast", "standard");
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-provider-label-fail", "sig-provider-label-fail");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-label-fail", "buyerlabelfail@example.com", "Buyer Label Fail", "Card", "card");
            var shipped = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Shipped);

            Assert.False(shipped.Success);
            var sellerOrder = await service.GetSellerOrderAsync(result.Order.Id, "seller-1");
            Assert.NotNull(sellerOrder);
            Assert.Equal(OrderStatuses.Paid, sellerOrder!.Status);
            Assert.False(sellerOrder.HasShippingLabel);
        }

        [Fact]
        public async Task UpdateShippingStatusFromProviderAsync_ShouldApplyProviderStatuses()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var providerOptions = new ShippingProviderOptions
            {
                Providers = new List<ShippingProviderDefinition>
                {
                    new()
                    {
                        Id = "shipfast",
                        Name = "ShipFast",
                        Services = new List<ShippingProviderServiceDefinition>
                        {
                            new() { Code = "standard", Name = "ShipFast Standard", TrackingUrlTemplate = "https://track.shipfast.test/{tracking}" }
                        }
                    }
                }
            };
            var shippingProviderService = new ShippingProviderService(providerOptions, TimeProvider.System, NullLogger<ShippingProviderService>.Instance);
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, shippingProviderService: shippingProviderService);
            var quote = BuildQuote("shipfast", "standard");
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-provider-2", "sig-provider-2");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-provider2", "buyerprovider2@example.com", "Buyer Provider2", "Card", "card");
            var shipped = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Shipped);

            var reference = shipped.UpdatedSubOrder!.ShippingProviderReference!;
            var trackingNumber = shipped.UpdatedSubOrder.TrackingNumber;

            var update = await service.UpdateShippingStatusFromProviderAsync("shipfast", reference, "delivered", trackingNumber, "ShipFast");
            Assert.True(update.Success);
            Assert.Equal(OrderStatuses.Delivered, update.UpdatedSubOrder!.Status);

            var buyerView = await service.GetOrderAsync(result.Order.Id, "buyer-provider2");
            Assert.NotNull(buyerView);
            Assert.Equal(OrderStatuses.Delivered, buyerView!.Status);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldReleaseEscrowOnCancellation()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-cancel-escrow", "sig-cancel-escrow");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-cancel-escrow", "cancel@example.com", "Cancel Buyer", "Card", "card");
            var cancel = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Cancelled);
            Assert.True(cancel.Success);

            var view = await service.GetOrderAsync(result.Order.Id, "buyer-cancel-escrow");
            var allocation = Assert.Single(view!.Escrow);
            var subOrder = Assert.Single(view.SubOrders);
            Assert.Equal(subOrder.GrandTotal, allocation.HeldAmount);
            Assert.Equal(allocation.HeldAmount, allocation.ReleasedToBuyer);
            Assert.Contains(allocation.Ledger, e => e.Type == EscrowEntryTypes.ReleaseToBuyer && e.Amount == allocation.HeldAmount);
            Assert.False(allocation.PayoutEligible);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldRecalculateCommissionOnPartialRefund()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var cartOptions = new CartOptions { PlatformCommissionRate = 0.1m, CommissionPrecision = 4 };
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, null, cartOptions);
            var quote = BuildMultiItemQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-commission-refund", "sig-commission-refund");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-commission-refund", "commissionrefund@example.com", "Commission Refund", "Card", "card");
            var initial = await service.GetOrderAsync(result.Order.Id, "buyer-commission-refund");
            var initialAllocation = Assert.Single(initial!.Escrow);
            Assert.Equal(2.8m, initialAllocation.CommissionAmount);
            Assert.Equal(30.2m, initialAllocation.SellerPayoutAmount);

            var refund = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Refunded, null, null, null, new[] { 2 });
            Assert.True(refund.Success);

            var refreshed = await service.GetOrderAsync(result.Order.Id, "buyer-commission-refund");
            var allocation = Assert.Single(refreshed!.Escrow);
            Assert.Equal(16, allocation.ReleasedToBuyer);
            Assert.Equal(1.2m, allocation.CommissionAmount);
            Assert.Equal(15.8m, allocation.SellerPayoutAmount);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldMarkEscrowPayoutEligibleFromConfig()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var escrowOptions = new EscrowOptions { PayoutEligibleStatuses = new List<string> { OrderStatuses.Shipped } };
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, escrowOptions);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-eligible-escrow", "sig-eligible-escrow");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-eligible", "eligible@example.com", "Eligible Buyer", "Card", "card");
            var initial = await service.GetOrderAsync(result.Order.Id, "buyer-eligible");
            var initialAllocation = Assert.Single(initial!.Escrow);
            Assert.False(initialAllocation.PayoutEligible);

            var shipped = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Shipped);
            Assert.True(shipped.Success);

            var view = await service.GetOrderAsync(result.Order.Id, "buyer-eligible");
            var allocation = Assert.Single(view!.Escrow);
            Assert.True(allocation.PayoutEligible);
            Assert.Contains(allocation.Ledger, e => e.Type == EscrowEntryTypes.PayoutEligible);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldSendShippedNotification_WithTracking()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-ship-email", "sig-ship-email");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-ship-email", "shipnotify@example.com", "Ship Notify", "Card", "card");
            Assert.True(result.Created);

            var shipped = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Shipped, "TRACK-EMAIL-1", null, "Carrier Email");
            Assert.True(shipped.Success);

            emailSender.Verify(
                e => e.SendEmailAsync(
                    "shipnotify@example.com",
                    It.Is<string>(s => s.Contains("shipped", StringComparison.OrdinalIgnoreCase)),
                    It.Is<string>(body => body.Contains("TRACK-EMAIL-1") && body.Contains("Carrier Email"))),
                Times.Once);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldTrackStatusHistory()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-status-history", "sig-status-history");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-history", "history@example.com", "History Buyer", "Card", "card");
            Assert.True(result.Created);

            var preparing = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Preparing);
            Assert.True(preparing.Success);

            var shipped = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Shipped, "TRACK-HIST", null, "Carrier Hist");
            Assert.True(shipped.Success);

            var delivered = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            Assert.True(delivered.Success);

            var sellerOrder = await service.GetSellerOrderAsync(result.Order.Id, "seller-1");
            Assert.NotNull(sellerOrder);
            var history = sellerOrder!.StatusHistory;
            Assert.True(history.Count >= 3);
            Assert.Contains(history, h => h.Status == OrderStatuses.Shipped && h.TrackingNumber == "TRACK-HIST");
            Assert.Equal(OrderStatuses.Delivered, history.Last().Status);
        }

        [Fact]
        public async Task RunSellerPayoutsAsync_ShouldProcessEligibleWeeklyPayout()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var escrowOptions = new EscrowOptions
            {
                PayoutEligibleStatuses = new List<string> { OrderStatuses.Delivered },
                DefaultPayoutSchedule = PayoutSchedules.Weekly,
                MinimumPayoutAmount = 0,
                PayoutBatchSize = 10
            };
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, escrowOptions);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-payout-1", "sig-payout-1");

            var creation = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-payout", "payout@example.com", "Payout Buyer", "Card", "card", OrderStatuses.Delivered, PaymentStatuses.Paid);
            Assert.True(creation.Created);

            var payout = await service.RunSellerPayoutsAsync("seller-1");

            Assert.True(payout.Success);
            Assert.Equal(PayoutStatuses.Paid, payout.Status);
            Assert.True(payout.ProcessedAmount > 0);
            var sellerOrder = await service.GetSellerOrderAsync(creation.Order.Id, "seller-1");
            Assert.NotNull(sellerOrder);
            Assert.NotNull(sellerOrder!.Escrow);
            Assert.Equal(PayoutStatuses.Paid, sellerOrder.Escrow!.PayoutStatus);
            Assert.Equal(sellerOrder.Escrow.SellerPayoutAmount, sellerOrder.Escrow.ReleasedToSeller);
        }

        [Fact]
        public async Task RunSellerPayoutsAsync_ShouldRolloverBelowThreshold()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var escrowOptions = new EscrowOptions
            {
                PayoutEligibleStatuses = new List<string> { OrderStatuses.Delivered },
                DefaultPayoutSchedule = PayoutSchedules.Weekly,
                MinimumPayoutAmount = 100,
                PayoutBatchSize = 10
            };
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, escrowOptions);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-payout-2", "sig-payout-2");

            var creation = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-payout-threshold", "payout-th@example.com", "Payout Buyer", "Card", "card", OrderStatuses.Delivered, PaymentStatuses.Paid);
            Assert.True(creation.Created);

            var payout = await service.RunSellerPayoutsAsync("seller-1");

            Assert.True(payout.Success);
            Assert.Equal(PayoutStatuses.Scheduled, payout.Status);
            Assert.Equal(0, payout.ProcessedAmount);
            var sellerOrder = await service.GetSellerOrderAsync(creation.Order.Id, "seller-1");
            Assert.NotNull(sellerOrder);
            Assert.NotNull(sellerOrder!.Escrow);
            Assert.Equal(PayoutStatuses.Scheduled, sellerOrder.Escrow!.PayoutStatus);
            Assert.Equal(0, sellerOrder.Escrow.ReleasedToSeller);
        }

        [Fact]
        public async Task GetPayoutsForSellerAsync_ShouldFilterByStatusAndReason()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;
            var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            var paidState = new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-payout-hist-1", "sig-payout-hist-1");
            var paidOrder = await service.EnsureOrderAsync(paidState, BuildQuote(), TestAddress, "buyer-hist", "hist@example.com", "Hist Buyer", "Card", "card", OrderStatuses.Delivered, PaymentStatuses.Paid);
            await service.RunSellerPayoutsAsync("seller-1");

            var paidRecord = context.Orders.Single(o => o.Id == paidOrder.Order.Id);
            var paidDetails = JsonSerializer.Deserialize<OrderDetailsPayload>(paidRecord.DetailsJson, serializerOptions)!;
            var paidAllocation = Assert.Single(paidDetails.Escrow);
            paidAllocation = paidAllocation with
            {
                Ledger = paidAllocation.Ledger.Select(l => l.Type == EscrowEntryTypes.PayoutEligible ? l with { RecordedOn = now.AddDays(-1) } : l).ToList()
            };
            paidDetails = paidDetails with { Escrow = new List<EscrowAllocation> { paidAllocation } };
            paidRecord.DetailsJson = JsonSerializer.Serialize(paidDetails, serializerOptions);
            paidRecord.CreatedOn = now.AddDays(-1);

            var failedState = new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-payout-hist-2", "sig-payout-hist-2");
            var failedOrder = await service.EnsureOrderAsync(failedState, BuildQuote(), TestAddress, "buyer-hist", "hist@example.com", "Hist Buyer", "Card", "card", OrderStatuses.Delivered, PaymentStatuses.Paid);

            var failedRecord = context.Orders.Single(o => o.Id == failedOrder.Order.Id);
            var failedDetails = JsonSerializer.Deserialize<OrderDetailsPayload>(failedRecord.DetailsJson, serializerOptions)!;
            if (failedDetails.Escrow == null || failedDetails.Escrow.Count == 0)
            {
                var failedView = await service.GetSellerOrderAsync(failedOrder.Order.Id, "seller-1");
                var fallbackAllocation = failedView?.Escrow ?? paidAllocation;
                failedDetails = failedDetails with { Escrow = new List<EscrowAllocation> { fallbackAllocation! } };
            }

            var failedAllocation = Assert.Single(failedDetails.Escrow);
            failedAllocation = failedAllocation with
            {
                PayoutStatus = PayoutStatuses.Failed,
                PayoutErrorReference = "bank_rejected",
                Ledger = failedAllocation.Ledger.Select(l => l with { RecordedOn = now }).ToList()
            };
            failedDetails = failedDetails with { Escrow = new List<EscrowAllocation> { failedAllocation } };
            failedRecord.DetailsJson = JsonSerializer.Serialize(failedDetails, serializerOptions);
            failedRecord.CreatedOn = now;
            await context.SaveChangesAsync();

            var allPayouts = await service.GetPayoutsForSellerAsync("seller-1", null, 1, 10);
            Assert.Equal(2, allPayouts.TotalCount);
            Assert.Contains(allPayouts.Items, p => p.Status == PayoutStatuses.Failed);

            var filters = new SellerPayoutFilterOptions
            {
                Statuses = new List<string> { PayoutStatuses.Failed },
                FromDate = now.AddDays(-2),
                ToDate = now.AddDays(1)
            };

            var paged = await service.GetPayoutsForSellerAsync("seller-1", filters, 1, 10);

            Assert.Equal(1, paged.TotalCount);
            var summary = Assert.Single(paged.Items);
            Assert.Equal(PayoutStatuses.Failed, summary.Status);
            Assert.Equal("bank_rejected", summary.ErrorReference);

            var detail = await service.GetSellerPayoutAsync(summary.OrderId, "seller-1");
            Assert.NotNull(detail);
            Assert.Equal(summary.SubOrderNumber, detail!.SubOrder.SubOrderNumber);
            Assert.Equal(PayoutStatuses.Failed, detail.Status);
            Assert.Equal("bank_rejected", detail.ErrorReference);
        }

        [Fact]
        public async Task GetMonthlySettlementsAsync_ShouldSummarizeAndFlagAdjustments()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var settlementOptions = new SettlementOptions { CloseDay = 1, TimeZone = "UTC" };
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, null, null, settlementOptions);
            var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            var windowEnd = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

            var januaryState = new CheckoutState("profile", TestAddress, windowEnd.AddDays(-10), new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-settle-jan", "sig-settle-jan");
            var januaryOrder = await service.EnsureOrderAsync(januaryState, BuildQuote(), TestAddress, "buyer-settle-jan", "settlejan@example.com", "Settle Jan", "Card", "card", OrderStatuses.Paid, PaymentStatuses.Paid);

            var januaryRecord = context.Orders.Single(o => o.Id == januaryOrder.Order.Id);
            var januaryDetails = JsonSerializer.Deserialize<OrderDetailsPayload>(januaryRecord.DetailsJson, serializerOptions)!;
            var januaryAllocation = Assert.Single(januaryDetails.Escrow);
            januaryAllocation = januaryAllocation with
            {
                PayoutStatus = PayoutStatuses.Paid,
                ReleasedToSeller = januaryAllocation.SellerPayoutAmount,
                PayoutEligible = true,
                Ledger = januaryAllocation.Ledger
                    .Select(l => l with { RecordedOn = windowEnd.AddDays(-5) })
                    .Append(new EscrowLedgerEntry(januaryAllocation.SubOrderNumber, januaryAllocation.SellerId, EscrowEntryTypes.PayoutEligible, januaryAllocation.SellerPayoutAmount, "Settlement ready", windowEnd.AddDays(-5)))
                    .ToList()
            };
            januaryDetails = januaryDetails with { Escrow = new List<EscrowAllocation> { januaryAllocation } };
            januaryRecord.DetailsJson = JsonSerializer.Serialize(januaryDetails, serializerOptions);
            januaryRecord.CreatedOn = windowEnd.AddDays(-12);

            var decemberState = new CheckoutState("profile", TestAddress, windowEnd.AddMonths(-1).AddDays(-10), new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-settle-dec", "sig-settle-dec");
            var decemberOrder = await service.EnsureOrderAsync(decemberState, BuildQuote(), TestAddress, "buyer-settle-dec", "settledec@example.com", "Settle Dec", "Card", "card", OrderStatuses.Paid, PaymentStatuses.Paid);

            var decemberRecord = context.Orders.Single(o => o.Id == decemberOrder.Order.Id);
            var decemberDetails = JsonSerializer.Deserialize<OrderDetailsPayload>(decemberRecord.DetailsJson, serializerOptions)!;
            var decemberAllocation = Assert.Single(decemberDetails.Escrow);
            decemberAllocation = decemberAllocation with
            {
                PayoutStatus = PayoutStatuses.Processing,
                PayoutEligible = true,
                Ledger = decemberAllocation.Ledger
                    .Select(l => l with { RecordedOn = windowEnd.AddDays(-3) })
                    .Append(new EscrowLedgerEntry(decemberAllocation.SubOrderNumber, decemberAllocation.SellerId, EscrowEntryTypes.PayoutEligible, decemberAllocation.SellerPayoutAmount, "Settlement adjustment", windowEnd.AddDays(-3)))
                    .ToList()
            };
            decemberDetails = decemberDetails with { Escrow = new List<EscrowAllocation> { decemberAllocation } };
            decemberRecord.DetailsJson = JsonSerializer.Serialize(decemberDetails, serializerOptions);
            decemberRecord.CreatedOn = windowEnd.AddMonths(-1).AddDays(-12);

            var februaryState = new CheckoutState("profile", TestAddress, windowEnd.AddDays(5), new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-settle-feb", "sig-settle-feb");
            var februaryOrder = await service.EnsureOrderAsync(februaryState, BuildQuote(), TestAddress, "buyer-settle-feb", "settlefeb@example.com", "Settle Feb", "Card", "card", OrderStatuses.Paid, PaymentStatuses.Paid);
            var februaryRecord = context.Orders.Single(o => o.Id == februaryOrder.Order.Id);
            var februaryDetails = JsonSerializer.Deserialize<OrderDetailsPayload>(februaryRecord.DetailsJson, serializerOptions)!;
            var februaryAllocation = Assert.Single(februaryDetails.Escrow);
            februaryAllocation = februaryAllocation with
            {
                PayoutEligible = true,
                Ledger = februaryAllocation.Ledger
                    .Select(l => l with { RecordedOn = windowEnd.AddDays(2) })
                    .Append(new EscrowLedgerEntry(februaryAllocation.SubOrderNumber, februaryAllocation.SellerId, EscrowEntryTypes.PayoutEligible, februaryAllocation.SellerPayoutAmount, "Next window", windowEnd.AddDays(2)))
                    .ToList()
            };
            februaryDetails = februaryDetails with { Escrow = new List<EscrowAllocation> { februaryAllocation } };
            februaryRecord.DetailsJson = JsonSerializer.Serialize(februaryDetails, serializerOptions);
            februaryRecord.CreatedOn = windowEnd.AddDays(2);

            await context.SaveChangesAsync();

            var summaries = await service.GetMonthlySettlementsAsync(windowEnd.Year, windowEnd.Month);
            var summary = Assert.Single(summaries);
            Assert.Equal("seller-1", summary.SellerId);
            Assert.Equal(2, summary.OrderCount);
            Assert.Equal(1, summary.AdjustmentCount);
            Assert.Equal(windowEnd.AddMonths(-1), summary.PeriodStart);
            Assert.Equal(windowEnd, summary.PeriodEnd);
            Assert.True(summary.GrossTotal >= 50);
            Assert.True(summary.PayoutTotal > 0);
            Assert.True(summary.AdjustmentTotal > 0);

            var detail = await service.GetMonthlySettlementDetailAsync(windowEnd.Year, windowEnd.Month, "seller-1");
            Assert.NotNull(detail);
            Assert.Equal(2, detail!.Orders.Count);
            Assert.Contains(detail.Orders, o => o.IsAdjustment);
            Assert.DoesNotContain(detail.Orders, o => o.PayoutOn >= summary.PeriodEnd);
        }

        [Fact]
        public async Task GetCommissionInvoicesForSellerAsync_ShouldExposeMonthlyInvoiceAndPdf()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var settlementOptions = new SettlementOptions { CloseDay = 1, TimeZone = "UTC" };
            var invoiceOptions = new InvoiceOptions { Series = "INV", TaxRate = 0.2m, HistoryMonths = 3 };
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, null, null, settlementOptions, invoiceOptions);
            var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            var windowEnd = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

            var januaryState = new CheckoutState("profile", TestAddress, windowEnd.AddDays(-10), new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-inv-jan", "sig-inv-jan");
            var januaryOrder = await service.EnsureOrderAsync(januaryState, BuildQuote(), TestAddress, "buyer-inv-jan", "invjan@example.com", "Invoice Jan", "Card", "card", OrderStatuses.Paid, PaymentStatuses.Paid);
            var januaryRecord = context.Orders.Single(o => o.Id == januaryOrder.Order.Id);
            var januaryDetails = JsonSerializer.Deserialize<OrderDetailsPayload>(januaryRecord.DetailsJson, serializerOptions)!;
            var januaryAllocation = Assert.Single(januaryDetails.Escrow);
            januaryAllocation = januaryAllocation with
            {
                PayoutStatus = PayoutStatuses.Paid,
                ReleasedToSeller = januaryAllocation.SellerPayoutAmount,
                PayoutEligible = true,
                Ledger = januaryAllocation.Ledger
                    .Select(l => l with { RecordedOn = windowEnd.AddDays(-5) })
                    .Append(new EscrowLedgerEntry(januaryAllocation.SubOrderNumber, januaryAllocation.SellerId, EscrowEntryTypes.PayoutEligible, januaryAllocation.SellerPayoutAmount, "Settlement ready", windowEnd.AddDays(-5)))
                    .ToList()
            };
            januaryDetails = januaryDetails with { Escrow = new List<EscrowAllocation> { januaryAllocation } };
            januaryRecord.DetailsJson = JsonSerializer.Serialize(januaryDetails, serializerOptions);
            januaryRecord.CreatedOn = windowEnd.AddDays(-12);

            var decemberState = new CheckoutState("profile", TestAddress, windowEnd.AddMonths(-1).AddDays(-10), new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-inv-dec", "sig-inv-dec");
            var decemberOrder = await service.EnsureOrderAsync(decemberState, BuildQuote(), TestAddress, "buyer-inv-dec", "invdec@example.com", "Invoice Dec", "Card", "card", OrderStatuses.Paid, PaymentStatuses.Paid);
            var decemberRecord = context.Orders.Single(o => o.Id == decemberOrder.Order.Id);
            var decemberDetails = JsonSerializer.Deserialize<OrderDetailsPayload>(decemberRecord.DetailsJson, serializerOptions)!;
            var decemberAllocation = Assert.Single(decemberDetails.Escrow);
            decemberAllocation = decemberAllocation with
            {
                PayoutStatus = PayoutStatuses.Processing,
                PayoutEligible = true,
                Ledger = decemberAllocation.Ledger
                    .Select(l => l with { RecordedOn = windowEnd.AddDays(-3) })
                    .Append(new EscrowLedgerEntry(decemberAllocation.SubOrderNumber, decemberAllocation.SellerId, EscrowEntryTypes.PayoutEligible, decemberAllocation.SellerPayoutAmount, "Settlement adjustment", windowEnd.AddDays(-3)))
                    .ToList()
            };
            decemberDetails = decemberDetails with { Escrow = new List<EscrowAllocation> { decemberAllocation } };
            decemberRecord.DetailsJson = JsonSerializer.Serialize(decemberDetails, serializerOptions);
            decemberRecord.CreatedOn = windowEnd.AddMonths(-1).AddDays(-12);

            var februaryState = new CheckoutState("profile", TestAddress, windowEnd.AddDays(5), new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-inv-feb", "sig-inv-feb");
            var februaryOrder = await service.EnsureOrderAsync(februaryState, BuildQuote(), TestAddress, "buyer-inv-feb", "invfeb@example.com", "Invoice Feb", "Card", "card", OrderStatuses.Paid, PaymentStatuses.Paid);
            var februaryRecord = context.Orders.Single(o => o.Id == februaryOrder.Order.Id);
            var februaryDetails = JsonSerializer.Deserialize<OrderDetailsPayload>(februaryRecord.DetailsJson, serializerOptions)!;
            var februaryAllocation = Assert.Single(februaryDetails.Escrow);
            februaryAllocation = februaryAllocation with
            {
                PayoutEligible = true,
                Ledger = februaryAllocation.Ledger
                    .Select(l => l with { RecordedOn = windowEnd.AddDays(2) })
                    .Append(new EscrowLedgerEntry(februaryAllocation.SubOrderNumber, februaryAllocation.SellerId, EscrowEntryTypes.PayoutEligible, februaryAllocation.SellerPayoutAmount, "Next window", windowEnd.AddDays(2)))
                    .ToList()
            };
            februaryDetails = februaryDetails with { Escrow = new List<EscrowAllocation> { februaryAllocation } };
            februaryRecord.DetailsJson = JsonSerializer.Serialize(februaryDetails, serializerOptions);
            februaryRecord.CreatedOn = windowEnd.AddDays(2);

            await context.SaveChangesAsync();

            var invoices = await service.GetCommissionInvoicesForSellerAsync("seller-1", 2);
            var invoice = Assert.Single(invoices);
            var expectedPrefix = $"{invoiceOptions.Series}-{DateTimeOffset.UtcNow:yyyyMM}-";
            Assert.StartsWith(expectedPrefix, invoice.InvoiceNumber);
            Assert.True(invoice.HasCorrections);
            Assert.Equal(invoice.NetAmount + invoice.TaxAmount, invoice.TotalAmount);
            Assert.Equal(InvoiceStatuses.Pending, invoice.Status);
            Assert.True(invoice.TaxAmount > 0);

            var pdf = await service.GetCommissionInvoicePdfAsync(invoice.InvoiceNumber, "seller-1");
            Assert.NotNull(pdf);
            Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf!.Content.Take(4).ToArray()));
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldUpdateSelectedItemsWithoutAffectingOthers()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildMultiItemQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-partial-1", "sig-partial-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-partial-1", "partial1@example.com", "Partial Buyer", "Card", "card");

            var partial = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Shipped, null, null, null, new[] { 1 });
            Assert.True(partial.Success);
            Assert.Equal(OrderStatuses.Shipped, partial.UpdatedSubOrder!.Status);
            var shippedItem = partial.UpdatedSubOrder.Items.First(i => i.ProductId == 1);
            var pendingItem = partial.UpdatedSubOrder.Items.First(i => i.ProductId == 2);
            Assert.Equal(OrderStatuses.Shipped, shippedItem.Status);
            Assert.Equal(OrderStatuses.Paid, pendingItem.Status);

            var sellerOrder = await service.GetSellerOrderAsync(result.Order.Id, "seller-1");
            Assert.NotNull(sellerOrder);
            Assert.Equal(OrderStatuses.Paid, sellerOrder!.Items.First(i => i.ProductId == 2).Status);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldCalculateRefundForCancelledItems()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildMultiItemQuote(4);
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-partial-2", "sig-partial-2");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-partial-2", "partial2@example.com", "Partial Buyer Two", "Card", "card");

            var cancel = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Cancelled, null, null, null, new[] { 2 });
            Assert.True(cancel.Success);
            Assert.Equal(OrderStatuses.Paid, cancel.UpdatedSubOrder!.Status);

            var refund = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Refunded, null, null, null, new[] { 2 });
            Assert.True(refund.Success);
            var updated = refund.UpdatedSubOrder!;
            var refundedItem = updated.Items.First(i => i.ProductId == 2);
            Assert.Equal(OrderStatuses.Refunded, refundedItem.Status);

            var totalLines = updated.Items.Sum(i => i.LineTotal);
            var targetLine = refundedItem.LineTotal;
            var discountShare = totalLines <= 0 ? 0 : Math.Min(targetLine, updated.DiscountTotal * (targetLine / totalLines));
            var expectedRefund = Math.Round(Math.Max(0, targetLine - discountShare), 2, MidpointRounding.AwayFromZero);
            Assert.Equal(expectedRefund, updated.RefundedAmount);
        }

        [Fact]
        public async Task CreateReturnRequestAsync_ShouldCreateRequestForDeliveredSubOrder()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-ret-1", "sig-ret-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-ret-1", "buyerret1@example.com", "Return Buyer", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-ret-1");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);

            var request = await service.CreateReturnRequestAsync(result.Order.Id, "buyer-ret-1", subOrderNumber, new List<int> { 1 }, "Item damaged", ReturnRequestTypes.Return, "Corner dented");
            Assert.True(request.Success);

            var refreshed = await service.GetOrderAsync(result.Order.Id, "buyer-ret-1");
            var subOrder = Assert.Single(refreshed!.SubOrders);
            Assert.NotNull(subOrder.Return);
            Assert.Equal(ReturnRequestStatuses.PendingSellerReview, subOrder.Return!.Status);
            Assert.Equal(ReturnRequestTypes.Return, subOrder.Return.Type);
            Assert.False(string.IsNullOrWhiteSpace(subOrder.Return.CaseId));
            Assert.Equal("Item damaged", subOrder.Return.Reason);
            Assert.Equal("Corner dented", subOrder.Return.Description);
            Assert.Single(subOrder.Return.Items);
            Assert.Equal(1, subOrder.Return.Items.First().ProductId);
        }

        [Fact]
        public async Task CreateReturnRequestAsync_ShouldCreateComplaintOutsideReturnWindow()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-complaint-1", "sig-complaint-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-complaint-1", "complaint@example.com", "Complaint Buyer", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-complaint-1");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);

            var record = context.Orders.Single(o => o.Id == result.Order.Id);
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var details = JsonSerializer.Deserialize<OrderDetailsPayload>(record.DetailsJson, options)!;
            var outdated = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(ReturnPolicies.ReturnWindowDays + 2));
            details = details with { SubOrders = details.SubOrders.Select(s => s with { DeliveredOn = outdated }).ToList() };
            record.DetailsJson = JsonSerializer.Serialize(details, options);
            await context.SaveChangesAsync();

            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-complaint-1",
                subOrderNumber,
                new List<int> { 1 },
                "Item defective",
                ReturnRequestTypes.Complaint,
                "Screen cracked");

            Assert.True(request.Success);

            var refreshed = await service.GetOrderAsync(result.Order.Id, "buyer-complaint-1");
            var subOrder = Assert.Single(refreshed!.SubOrders);
            Assert.NotNull(subOrder.Return);
            Assert.Equal(ReturnRequestTypes.Complaint, subOrder.Return!.Type);
            Assert.Equal(ReturnRequestStatuses.PendingSellerReview, subOrder.Return.Status);
            Assert.Equal("Screen cracked", subOrder.Return.Description);
        }

        [Fact]
        public async Task CreateReturnRequestAsync_ShouldRejectWhenOutsideWindow()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-ret-2", "sig-ret-2");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-ret-2", "buyerret2@example.com", "Return Buyer Two", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-ret-2");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;
            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);

            var record = context.Orders.Single(o => o.Id == result.Order.Id);
            var details = JsonSerializer.Deserialize<OrderDetailsPayload>(record.DetailsJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
            var outdated = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(ReturnPolicies.ReturnWindowDays + 1));
            details = details with { SubOrders = details.SubOrders.Select(s => s with { DeliveredOn = outdated }).ToList() };
            record.DetailsJson = JsonSerializer.Serialize(details, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await context.SaveChangesAsync();

            var request = await service.CreateReturnRequestAsync(result.Order.Id, "buyer-ret-2", subOrderNumber, new List<int> { 1 }, "Too late", ReturnRequestTypes.Return, "Outside window");
            Assert.False(request.Success);
        }

        [Fact]
        public async Task GetReturnCasesForBuyerAsync_ShouldReturnCasesWithBasicInfo()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-cases-1", "sig-cases-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-cases-1", "cases@example.com", "Case Buyer", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-cases-1");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);

            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-cases-1",
                subOrderNumber,
                new List<int> { 1 },
                "Wrong size",
                ReturnRequestTypes.Return,
                "Too small");

            var cases = await service.GetReturnCasesForBuyerAsync("buyer-cases-1");
            var summary = Assert.Single(cases.Items);
            Assert.Equal(request.Request!.CaseId, summary.CaseId);
            Assert.Equal(result.Order.Id, summary.OrderId);
            Assert.Equal(subOrderNumber, summary.SubOrderNumber);
            Assert.Equal(ReturnRequestStatuses.PendingSellerReview, summary.Status);
            Assert.Equal(orderView.SubOrders.Single().SellerName, summary.SellerName);
        }

        [Fact]
        public async Task GetReturnCaseForBuyerAsync_ShouldReflectResolutionAfterRefund()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-cases-2", "sig-cases-2");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-cases-2", "case2@example.com", "Case Two", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-cases-2");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-cases-2",
                subOrderNumber,
                new List<int> { 1 },
                "Damaged",
                ReturnRequestTypes.Return,
                "Broken screen");

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Refunded, null, 5m);

            var detail = await service.GetReturnCaseForBuyerAsync("buyer-cases-2", request.Request!.CaseId!);
            Assert.NotNull(detail);
            Assert.Equal(ReturnRequestStatuses.Completed, detail!.Summary.Status);
            Assert.Equal(5m, detail.Resolution.RefundedAmount);
            Assert.Equal(result.Order.PaymentReference, detail.Resolution.PaymentReference);
            Assert.True(detail.Resolution.Outcome.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                || detail.Resolution.Outcome.Equals("Partially approved", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task GetReturnCasesForSellerAsync_ShouldReturnCasesForSeller()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-cases-seller-1", "sig-cases-seller-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-cases-seller", "cases@example.com", "Case Buyer", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-cases-seller");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-cases-seller",
                subOrderNumber,
                new List<int> { 1 },
                "Wrong size",
                ReturnRequestTypes.Return,
                "Too small");

            var cases = await service.GetReturnCasesForSellerAsync("seller-1");
            var summary = Assert.Single(cases.Items);
            Assert.Equal(request.Request!.CaseId, summary.CaseId);
            Assert.Equal(result.Order.Id, summary.OrderId);
            Assert.Equal(subOrderNumber, summary.SubOrderNumber);
            Assert.Equal(ReturnRequestStatuses.PendingSellerReview, summary.Status);
            Assert.Equal("Case Buyer", summary.BuyerName);
        }

        [Fact]
        public async Task UpdateReturnCaseForSellerAsync_ShouldUpdateStatusAndNotifyBuyer()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-update-case-1", "sig-update-case-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-update-case", "caseupdate@example.com", "Case Update Buyer", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-update-case");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-update-case",
                subOrderNumber,
                new List<int> { 1 },
                "Damaged",
                ReturnRequestTypes.Return,
                "Broken screen");

            var update = await service.UpdateReturnCaseForSellerAsync(
                result.Order.Id,
                "seller-1",
                request.Request!.CaseId!,
                "accept",
                "Approved after review");

            Assert.True(update.Success);
            Assert.NotNull(update.Request);
            Assert.Equal(ReturnRequestStatuses.Approved, update.Request!.Status);

            var detail = await service.GetReturnCaseForSellerAsync("seller-1", request.Request!.CaseId!);
            Assert.NotNull(detail);
            Assert.Equal(ReturnRequestStatuses.Approved, detail!.Summary.Status);
            Assert.Contains(detail.History, h => h.Actor.Equals("Seller", StringComparison.OrdinalIgnoreCase) && h.Status == ReturnRequestStatuses.Approved);

            emailSender.Verify(s => s.SendEmailAsync(
                "caseupdate@example.com",
                It.Is<string>(subject => subject.Contains(request.Request!.CaseId!, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public async Task ResolveReturnCaseForSellerAsync_ShouldApplyPartialRefundAndLinkReference()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-resolve-1", "sig-resolve-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-resolve", "resolve@example.com", "Resolve Buyer", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-resolve");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-resolve",
                subOrderNumber,
                new List<int> { 1 },
                "Damaged",
                ReturnRequestTypes.Return,
                "Broken glass");

            var resolved = await service.ResolveReturnCaseForSellerAsync(
                result.Order.Id,
                "seller-1",
                request.Request!.CaseId!,
                "partialrefund",
                5m,
                "refund-123",
                "Offering partial refund");

            Assert.True(resolved.Success);
            Assert.NotNull(resolved.Request);
            Assert.Equal(ReturnRequestStatuses.Completed, resolved.Request!.Status);
            Assert.Equal("Partial refund", resolved.Request!.ResolutionOutcome);
            Assert.Equal(5m, resolved.Request!.ResolutionRefundAmount);

            var sellerDetail = await service.GetReturnCaseForSellerAsync("seller-1", request.Request!.CaseId!);
            Assert.NotNull(sellerDetail);
            Assert.Equal("refund-123", sellerDetail!.Resolution.PaymentReference);
            Assert.Equal(PaymentStatuses.Refunded, sellerDetail.Resolution.PaymentStatus);
            Assert.Equal(5m, sellerDetail.Resolution.RefundedAmount);
            Assert.Equal("Partial refund", sellerDetail.Resolution.Outcome);
        }

        [Fact]
        public async Task ResolveReturnCaseForSellerAsync_NoRefund_ShouldExposeReasonToBuyer()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-resolve-2", "sig-resolve-2");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-resolve-nr", "resolveno@example.com", "Resolve No Refund", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-resolve-nr");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-resolve-nr",
                subOrderNumber,
                new List<int> { 1 },
                "Not eligible",
                ReturnRequestTypes.Return,
                "Used item");

            var resolved = await service.ResolveReturnCaseForSellerAsync(
                result.Order.Id,
                "seller-1",
                request.Request!.CaseId!,
                "norefund",
                null,
                null,
                "Item shows heavy wear");

            Assert.True(resolved.Success);
            Assert.NotNull(resolved.Request);
            Assert.Equal(ReturnRequestStatuses.Rejected, resolved.Request!.Status);
            Assert.Equal("No refund", resolved.Request!.ResolutionOutcome);
            Assert.Equal("Item shows heavy wear", resolved.Request!.ResolutionNote);

            var buyerDetail = await service.GetReturnCaseForBuyerAsync("buyer-resolve-nr", request.Request!.CaseId!);
            Assert.NotNull(buyerDetail);
            Assert.Equal(ReturnRequestStatuses.Rejected, buyerDetail!.Summary.Status);
            Assert.Equal("No refund", buyerDetail.Resolution.Outcome);
            Assert.Equal("Item shows heavy wear", buyerDetail.Resolution.DecisionNote);
            Assert.Equal("Not required", buyerDetail.Resolution.PaymentStatus);
        }

        [Fact]
        public async Task AddReturnCaseMessages_ShouldBeSharedBetweenBuyerAndSeller()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-case-msg-1", "sig-case-msg-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-case-msg", "casemsg@example.com", "Case Message Buyer", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-case-msg");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-case-msg",
                subOrderNumber,
                new List<int> { 1 },
                "Damaged",
                ReturnRequestTypes.Return,
                "Screen cracked");

            var beforeSummary = (await service.GetReturnCasesForBuyerAsync("buyer-case-msg")).Items.Single();

            var buyerMessage = await service.AddReturnCaseMessageForBuyerAsync("buyer-case-msg", request.Request!.CaseId!, "Need an update on the resolution.");
            Assert.True(buyerMessage.Success);
            Assert.NotNull(buyerMessage.Request);
            Assert.Contains(buyerMessage.Request!.Messages, m => m.Actor == "Buyer" && m.Message.Contains("Need an update", StringComparison.OrdinalIgnoreCase));

            var sellerMessage = await service.AddReturnCaseMessageForSellerAsync("seller-1", request.Request!.CaseId!, "Please share photos of the damage.");
            Assert.True(sellerMessage.Success);

            var buyerDetail = await service.GetReturnCaseForBuyerAsync("buyer-case-msg", request.Request!.CaseId!);
            Assert.NotNull(buyerDetail);
            Assert.Equal(2, buyerDetail!.Messages.Count);
            Assert.Equal("Buyer", buyerDetail.Messages.First().Actor);
            Assert.Equal("Seller", buyerDetail.Messages.Last().Actor);

            var sellerDetail = await service.GetReturnCaseForSellerAsync("seller-1", request.Request!.CaseId!);
            Assert.NotNull(sellerDetail);
            Assert.Equal(2, sellerDetail!.Messages.Count);

            var summaryAfter = (await service.GetReturnCasesForBuyerAsync("buyer-case-msg")).Items.Single();
            Assert.True(summaryAfter.LastUpdatedOn >= summaryAfter.RequestedOn);
            Assert.True(summaryAfter.LastUpdatedOn > beforeSummary.LastUpdatedOn);
        }

        [Fact]
        public async Task AddReturnCaseMessageForSellerAsync_ShouldEnforceOwnership()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-case-msg-2", "sig-case-msg-2");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-case-msg2", "casemsg2@example.com", "Case Message Buyer 2", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-case-msg2");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-case-msg2",
                subOrderNumber,
                new List<int> { 1 },
                "Wrong item",
                ReturnRequestTypes.Return,
                "Received wrong color");

            var unauthorized = await service.AddReturnCaseMessageForSellerAsync("seller-2", request.Request!.CaseId!, "Attempted message");
            Assert.False(unauthorized.Success);
        }

        [Fact]
        public async Task GetReturnCasesForAdminAsync_ShouldReturnPlatformCases()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-admin-list", "sig-admin-list");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-admin", "adminlist@example.com", "Admin Buyer", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-admin");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-admin",
                subOrderNumber,
                new List<int> { 1 },
                "Damaged",
                ReturnRequestTypes.Complaint,
                "Complaint details");

            var paged = await service.GetReturnCasesForAdminAsync();
            Assert.Equal(1, paged.TotalCount);
            var summary = Assert.Single(paged.Items);
            Assert.Equal("seller-1", summary.SellerId);
            Assert.Equal(ReturnRequestTypes.Complaint, summary.Type);
            Assert.Equal(ReturnRequestStatuses.PendingSellerReview, summary.Status);
            Assert.False(string.IsNullOrWhiteSpace(summary.BuyerName));
        }

        [Fact]
        public async Task CreateReturnRequestAsync_ShouldPopulateSlaDeadlines()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var slaOptions = new CaseSlaOptions
            {
                DefaultFirstResponseHours = 12,
                DefaultResolutionHours = 48
            };
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, caseSlaOptions: slaOptions);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-sla-1", "sig-sla-1");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-sla-1", "sla1@example.com", "SLA Buyer", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-sla-1");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-sla-1",
                subOrderNumber,
                new List<int> { 1 },
                "Damaged",
                ReturnRequestTypes.Return,
                "Screen cracked");

            var adminDetail = await service.GetReturnCaseForAdminAsync(request.Request!.CaseId!);
            Assert.NotNull(adminDetail);
            Assert.True(adminDetail!.Summary.FirstResponseDueOn.HasValue);
            Assert.True(adminDetail.Summary.ResolutionDueOn.HasValue);
            var responseDelta = adminDetail.Summary.FirstResponseDueOn!.Value - adminDetail.Summary.RequestedOn;
            var resolutionDelta = adminDetail.Summary.ResolutionDueOn!.Value - adminDetail.Summary.RequestedOn;
            Assert.True(responseDelta.TotalHours <= 12.1);
            Assert.True(resolutionDelta.TotalHours <= 48.1);
        }

        [Fact]
        public async Task GetSellerSlaMetricsAsync_ShouldHighlightBreaches()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var slaOptions = new CaseSlaOptions
            {
                DefaultFirstResponseHours = 1,
                DefaultResolutionHours = 2
            };
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance, caseSlaOptions: slaOptions);
            var quote = BuildQuote();

            var fastState = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-sla-2a", "sig-sla-2a");
            var fastResult = await service.EnsureOrderAsync(fastState, quote, TestAddress, "buyer-sla-2a", "sla2a@example.com", "SLA Buyer 2A", "Card", "card");
            var fastOrderView = await service.GetOrderAsync(fastResult.Order.Id, "buyer-sla-2a");
            var fastSubOrderNumber = Assert.Single(fastOrderView!.SubOrders).SubOrderNumber;
            await service.UpdateSubOrderStatusAsync(fastResult.Order.Id, "seller-1", OrderStatuses.Delivered);
            var fastCase = await service.CreateReturnRequestAsync(
                fastResult.Order.Id,
                "buyer-sla-2a",
                fastSubOrderNumber,
                new List<int> { 1 },
                "Broken",
                ReturnRequestTypes.Return,
                "First case");
            await service.ResolveReturnCaseForSellerAsync(
                fastResult.Order.Id,
                "seller-1",
                fastCase.Request!.CaseId!,
                "replacement",
                null,
                null,
                "Quick resolution");

            var slowState = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-sla-2b", "sig-sla-2b");
            var slowResult = await service.EnsureOrderAsync(slowState, quote, TestAddress, "buyer-sla-2b", "sla2b@example.com", "SLA Buyer 2B", "Card", "card");
            var slowOrderView = await service.GetOrderAsync(slowResult.Order.Id, "buyer-sla-2b");
            var slowSubOrderNumber = Assert.Single(slowOrderView!.SubOrders).SubOrderNumber;
            await service.UpdateSubOrderStatusAsync(slowResult.Order.Id, "seller-1", OrderStatuses.Delivered);
            var slowCase = await service.CreateReturnRequestAsync(
                slowResult.Order.Id,
                "buyer-sla-2b",
                slowSubOrderNumber,
                new List<int> { 1 },
                "Noisy",
                ReturnRequestTypes.Return,
                "Second case");

            var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var slowRecord = await context.Orders.FirstAsync(o => o.Id == slowResult.Order.Id);
            var slowPayload = JsonSerializer.Deserialize<OrderDetailsPayload>(slowRecord.DetailsJson, serializerOptions)!;
            var agedRequestedOn = DateTimeOffset.UtcNow.AddHours(-5);
            var agedReturn = slowPayload.SubOrders.First().Return! with
            {
                RequestedOn = agedRequestedOn,
                History = new List<ReturnRequestHistoryEntry> { new(ReturnRequestStatuses.PendingSellerReview, "Buyer", agedRequestedOn, "Case opened") },
                FirstResponseDueOn = null,
                ResolutionDueOn = null,
                FirstRespondedOn = null,
                SlaBreached = false,
                SlaBreachedOn = null
            };
            slowPayload.SubOrders[0] = slowPayload.SubOrders.First() with { Return = agedReturn };
            slowRecord.DetailsJson = JsonSerializer.Serialize(slowPayload, serializerOptions);
            await context.SaveChangesAsync();

            await service.ResolveReturnCaseForSellerAsync(
                slowResult.Order.Id,
                "seller-1",
                slowCase.Request!.CaseId!,
                "repair",
                null,
                null,
                "Delayed resolution");

            var paged = await service.GetReturnCasesForAdminAsync();
            Assert.Equal(2, paged.TotalCount);
            var slowSummary = paged.Items.First(c => c.CaseId == slowCase.Request!.CaseId);
            Assert.True(slowSummary.IsSlaBreached);

            var metrics = await service.GetSellerSlaMetricsAsync();
            var sellerMetrics = Assert.Single(metrics);
            Assert.Equal(2, sellerMetrics.TotalCases);
            Assert.Equal(2, sellerMetrics.ResolvedCases);
            Assert.Equal(1, sellerMetrics.ResolvedWithinSla);
            Assert.True(sellerMetrics.ResolutionSlaRate < 100);
            Assert.True(sellerMetrics.AverageFirstResponseTime.HasValue);
        }

        [Fact]
        public async Task EscalateReturnCaseForAdminAsync_ShouldMoveCaseUnderReview_AndNotifyParties()
        {
            await using var context = CreateContext();
            context.Users.Add(new ApplicationUser
            {
                Id = "seller-1",
                UserName = "seller@example.com",
                Email = "seller@example.com",
                AccountType = AccountTypes.Seller,
                FullName = "Seller One",
                Address = "1 Admin Way",
                Country = "US"
            });
            await context.SaveChangesAsync();

            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-admin-escalate", "sig-admin-escalate");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-admin-esc", "buyeresc@example.com", "Buyer Esc", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-admin-esc");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-admin-esc",
                subOrderNumber,
                new List<int> { 1 },
                "Damaged",
                ReturnRequestTypes.Return,
                "Cracked screen");

            emailSender.Invocations.Clear();

            var escalated = await service.EscalateReturnCaseForAdminAsync(
                result.Order.Id,
                request.Request!.CaseId!,
                "buyer",
                "Buyer requested admin help");

            Assert.True(escalated.Success);
            Assert.NotNull(escalated.Request);
            Assert.Equal(ReturnRequestStatuses.UnderAdminReview, escalated.Request!.Status);
            Assert.Contains(escalated.Request!.History, h => h.Actor == "Admin" && h.Status == ReturnRequestStatuses.UnderAdminReview);

            var detail = await service.GetReturnCaseForAdminAsync(request.Request!.CaseId!);
            Assert.NotNull(detail);
            Assert.Equal(ReturnRequestStatuses.UnderAdminReview, detail!.Summary.Status);

            emailSender.Verify(e => e.SendEmailAsync("buyeresc@example.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            emailSender.Verify(e => e.SendEmailAsync("seller@example.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ResolveReturnCaseForAdminAsync_ShouldRecordAdminDecision()
        {
            await using var context = CreateContext();
            context.Users.Add(new ApplicationUser
            {
                Id = "seller-1",
                UserName = "seller@example.com",
                Email = "seller@example.com",
                AccountType = AccountTypes.Seller,
                FullName = "Seller One",
                Address = "1 Admin Way",
                Country = "US"
            });
            await context.SaveChangesAsync();

            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-admin-decision", "sig-admin-decision");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-admin-dec", "buyerdecision@example.com", "Buyer Decider", "Card", "card");
            var orderView = await service.GetOrderAsync(result.Order.Id, "buyer-admin-dec");
            var subOrderNumber = Assert.Single(orderView!.SubOrders).SubOrderNumber;

            await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Delivered);
            var request = await service.CreateReturnRequestAsync(
                result.Order.Id,
                "buyer-admin-dec",
                subOrderNumber,
                new List<int> { 1 },
                "Damaged",
                ReturnRequestTypes.Return,
                "Broken item");

            emailSender.Invocations.Clear();

            var resolved = await service.ResolveReturnCaseForAdminAsync(
                result.Order.Id,
                request.Request!.CaseId!,
                "fullrefund",
                12m,
                "admin-ref-1",
                "Override seller decision");

            Assert.True(resolved.Success);
            Assert.NotNull(resolved.Request);
            Assert.Equal(ReturnRequestStatuses.Completed, resolved.Request!.Status);
            Assert.Equal("Admin", resolved.Request!.ResolutionActor);
            Assert.Equal("Full refund", resolved.Request!.ResolutionOutcome);
            Assert.Equal(orderView.SubOrders.Single().GrandTotal, resolved.Request!.ResolutionRefundAmount);

            var detail = await service.GetReturnCaseForAdminAsync(request.Request!.CaseId!);
            Assert.NotNull(detail);
            Assert.Equal(ReturnRequestStatuses.Completed, detail!.Summary.Status);
            Assert.Contains(detail.History, h => h.Actor == "Admin" && h.Status == ReturnRequestStatuses.Completed);

            emailSender.Verify(e => e.SendEmailAsync("buyerdecision@example.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            emailSender.Verify(e => e.SendEmailAsync("seller@example.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task UpdateSubOrderStatusAsync_ShouldRejectInvalidTransition()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var quote = BuildQuote();
            var state = new CheckoutState("profile", TestAddress, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-11", "sig-11");

            var result = await service.EnsureOrderAsync(state, quote, TestAddress, "buyer-11", "buyer11@example.com", "Buyer Eleven", "Card", "card");

            var rejection = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Preparing);
            Assert.True(rejection.Success);

            var invalid = await service.UpdateSubOrderStatusAsync(result.Order.Id, "seller-1", OrderStatuses.Paid);
            Assert.False(invalid.Success);
            Assert.NotNull(invalid.Error);
        }

        [Fact]
        public async Task GetSummariesForBuyerAsync_ShouldFilterByStatusDateAndSeller()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;
            var buyerId = "buyer-filter";

            var first = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-f1", "sig-f1"),
                BuildQuote(),
                TestAddress,
                buyerId,
                "buyer-filter@example.com",
                "Buyer Filter",
                "Card",
                "card");
            first.Order.CreatedOn = now.AddDays(-10);

            var multi = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "express", ["seller-2"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-f2", "sig-f2"),
                BuildMultiSellerQuote(),
                TestAddress,
                buyerId,
                "buyer-filter@example.com",
                "Buyer Filter",
                "Card",
                "card");
            await service.UpdateSubOrderStatusAsync(multi.Order.Id, "seller-2", OrderStatuses.Shipped);
            multi.Order.CreatedOn = now.AddDays(-1);

            var recentOther = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-f3", "sig-f3"),
                BuildQuote(),
                TestAddress,
                buyerId,
                "buyer-filter@example.com",
                "Buyer Filter",
                "Card",
                "card");
            recentOther.Order.CreatedOn = now.AddDays(-2);

            await context.SaveChangesAsync();

            var filters = new BuyerOrderFilterOptions
            {
                Statuses = new List<string> { OrderStatuses.Shipped },
                FromDate = now.AddDays(-3),
                ToDate = now,
                SellerId = "seller-2"
            };

            var paged = await service.GetSummariesForBuyerAsync(buyerId, filters, 1, 10);

            Assert.Equal(1, paged.TotalCount);
            var summary = Assert.Single(paged.Items);
            Assert.Equal(multi.Order.Id, summary.Id);
            Assert.Equal(OrderStatuses.Shipped, summary.Status);
        }

        [Fact]
        public async Task GetSummariesForBuyerAsync_ShouldPaginateNewestFirst()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;
            var buyerId = "buyer-page";

            for (var i = 0; i < 12; i++)
            {
                var state = new CheckoutState("profile", TestAddress, now.AddMinutes(-i), new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, $"ref-p{i}", $"sig-p{i}");
                var result = await service.EnsureOrderAsync(state, BuildQuote(), TestAddress, buyerId, "buyer-page@example.com", "Buyer Page", "Card", "card");
                result.Order.CreatedOn = now.AddMinutes(-i);
            }

            await context.SaveChangesAsync();

            var paged = await service.GetSummariesForBuyerAsync(buyerId, null, 2, 5);

            Assert.Equal(12, paged.TotalCount);
            Assert.Equal(3, paged.TotalPages);
            Assert.Equal(5, paged.Items.Count);
            var expectedIds = context.Orders
                .Where(o => o.BuyerId == buyerId)
                .OrderByDescending(o => o.CreatedOn)
                .Skip(5)
                .Take(5)
                .Select(o => o.Id)
                .ToList();
            Assert.Equal(expectedIds, paged.Items.Select(i => i.Id).ToList());
        }

        [Fact]
        public async Task GetSummariesForSellerAsync_ShouldFilterByStatusDateAndBuyer()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;

            var older = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-sf1", "sig-sf1"),
                BuildQuote(),
                TestAddress,
                "buyer-sf-1",
                "alice@example.com",
                "Alice Buyer",
                "Card",
                "card");
            older.Order.CreatedOn = now.AddDays(-5);

            var match = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-sf2", "sig-sf2"),
                BuildQuote(),
                TestAddress,
                "buyer-sf-2",
                "bob@example.com",
                "Bob Buyer",
                "Card",
                "card");
            await service.UpdateSubOrderStatusAsync(match.Order.Id, "seller-1", OrderStatuses.Preparing);
            match.Order.CreatedOn = now.AddHours(-12);

            var differentStatus = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-sf3", "sig-sf3"),
                BuildQuote(),
                TestAddress,
                "buyer-sf-3",
                "bob@example.com",
                "Bob Buyer",
                "Card",
                "card");
            await service.UpdateSubOrderStatusAsync(differentStatus.Order.Id, "seller-1", OrderStatuses.Shipped);
            differentStatus.Order.CreatedOn = now.AddHours(-4);

            await context.SaveChangesAsync();

            var filters = new SellerOrderFilterOptions
            {
                Statuses = new List<string> { OrderStatuses.Preparing },
                FromDate = now.AddDays(-2),
                ToDate = now,
                BuyerQuery = "bob"
            };

            var paged = await service.GetSummariesForSellerAsync("seller-1", filters, 1, 10);

            Assert.Equal(1, paged.TotalCount);
            var summary = Assert.Single(paged.Items);
            Assert.Equal(match.Order.Id, summary.Id);
            Assert.Equal(OrderStatuses.Preparing, summary.Status);
            Assert.Equal("Bob Buyer", summary.BuyerName);
        }

        [Fact]
        public async Task GetSummariesForSellerAsync_ShouldPaginateNewestFirst()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;

            for (var i = 0; i < 12; i++)
            {
                var state = new CheckoutState("profile", TestAddress, now.AddMinutes(-i), new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, $"ref-sp{i}", $"sig-sp{i}");
                var result = await service.EnsureOrderAsync(state, BuildQuote(), TestAddress, $"buyer-sp-{i}", "seller-page@example.com", "Seller Page", "Card", "card");
                result.Order.CreatedOn = now.AddMinutes(-i);
            }

            await context.SaveChangesAsync();

            var paged = await service.GetSummariesForSellerAsync("seller-1", null, 2, 5);

            Assert.Equal(12, paged.TotalCount);
            Assert.Equal(3, paged.TotalPages);
            Assert.Equal(5, paged.Items.Count);
            var expectedIds = context.Orders
                .Where(o => o.DetailsJson.Contains("\"sellerId\":\"seller-1\""))
                .OrderByDescending(o => o.CreatedOn)
                .Skip(5)
                .Take(5)
                .Select(o => o.Id)
                .ToList();
            Assert.Equal(expectedIds, paged.Items.Select(i => i.Id).ToList());
        }

        [Fact]
        public async Task ExportSellerOrdersAsync_ShouldRespectFilters()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;

            var first = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-se1", "sig-se1"),
                BuildQuote(),
                TestAddress,
                "buyer-se-1",
                "export-one@example.com",
                "Export One",
                "Card",
                "card");
            first.Order.CreatedOn = now.AddDays(-1);

            var second = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-se2", "sig-se2"),
                BuildQuote(),
                TestAddress,
                "buyer-se-2",
                "export-two@example.com",
                "Export Two",
                "Card",
                "card");
            await service.UpdateSubOrderStatusAsync(second.Order.Id, "seller-1", OrderStatuses.Preparing);
            second.Order.CreatedOn = now;

            await context.SaveChangesAsync();

            var export = await service.ExportSellerOrdersAsync("seller-1", new SellerOrderFilterOptions
            {
                Statuses = new List<string> { OrderStatuses.Preparing },
                FromDate = now.AddDays(-2),
                ToDate = now.AddDays(1)
            });

            Assert.NotNull(export);
            Assert.False(export!.Truncated);
            Assert.Equal(1, export.RowCount);
            Assert.Equal(1, export.TotalMatching);

            var csv = Encoding.UTF8.GetString(export.Content);
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            var dataLine = lines[1].TrimEnd('\r');
            Assert.Contains(OrderStatuses.Preparing, dataLine);
            Assert.Contains(second.Order.OrderNumber, dataLine);
            Assert.Contains("export-two@example.com", dataLine);
            Assert.Contains("Standard", dataLine);
            Assert.Contains(TestAddress.Line1, dataLine);
            Assert.Contains(TestAddress.PostalCode, dataLine);
            Assert.Contains(TestAddress.Phone!, dataLine);
            Assert.Contains("ref-se2", dataLine);
        }

        [Fact]
        public async Task ExportSellerOrdersAsync_ShouldReturnNullWhenNoMatchingOrders()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);

            var export = await service.ExportSellerOrdersAsync("seller-1", new SellerOrderFilterOptions
            {
                Statuses = new List<string> { OrderStatuses.Delivered },
                FromDate = DateTimeOffset.UtcNow.AddDays(-1),
                ToDate = DateTimeOffset.UtcNow
            });

            Assert.Null(export);
        }

        [Fact]
        public async Task ExportSellerOrdersAsync_ShouldFilterMissingTrackingOnly()
        {
            await using var context = CreateContext();
            var emailSender = new Mock<IEmailSender>();
            var service = new OrderService(context, emailSender.Object, NullLogger<OrderService>.Instance);
            var now = DateTimeOffset.UtcNow;

            var untracked = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-no-track", "sig-no-track"),
                BuildQuote(),
                TestAddress,
                "buyer-no-track",
                "untracked@example.com",
                "No Tracking",
                "Card",
                "card");

            var tracked = await service.EnsureOrderAsync(
                new CheckoutState("profile", TestAddress, now, new Dictionary<string, string> { ["seller-1"] = "standard" }, "card", CheckoutPaymentStatus.Confirmed, "ref-track", "sig-track"),
                BuildQuote(),
                TestAddress,
                "buyer-track",
                "tracked@example.com",
                "Tracked Buyer",
                "Card",
                "card");
            await service.UpdateSubOrderStatusAsync(tracked.Order.Id, "seller-1", OrderStatuses.Shipped, "TRACK-123", trackingCarrier: "UPS");

            await context.SaveChangesAsync();

            var export = await service.ExportSellerOrdersAsync("seller-1", new SellerOrderFilterOptions
            {
                MissingTrackingOnly = true
            });

            Assert.NotNull(export);
            Assert.Equal(1, export!.RowCount);
            var csv = Encoding.UTF8.GetString(export.Content);
            Assert.Contains(untracked.Order.OrderNumber, csv);
            Assert.DoesNotContain(tracked.Order.OrderNumber, csv);
        }

        private class FailingLabelShippingProviderService : ShippingProviderService
        {
            public FailingLabelShippingProviderService(ShippingProviderOptions options, TimeProvider clock, ILogger<ShippingProviderService> logger)
                : base(options, clock, logger)
            {
            }

            protected override byte[] RenderLabelPdf(ShippingProviderShipmentRequest request, string trackingNumber, string carrier, string providerReference)
            {
                throw new InvalidOperationException("Simulated label failure");
            }
        }

        private static ShippingQuote BuildQuote(string? providerId = null, string? providerServiceCode = null)
        {
            var product = new ProductModel { Id = 1, Title = "Sample Item", Price = 10, Stock = 5, SellerId = "seller-1" };
            var displayItem = new CartDisplayItem(product, 2, "Red", 10, 20, true, 5, new Dictionary<string, string>());
            var sellerGroup = new CartSellerGroup("seller-1", "Seller One", 20, 5, 25, new List<CartDisplayItem> { displayItem });
            var summary = new CartSummary(new List<CartSellerGroup> { sellerGroup }, 20, 5, 25, 2, CartSettlementSummary.Empty);
            var options = new List<ShippingMethodOption> { new("standard", "Standard", 5, "Standard delivery", true, null, providerId, providerServiceCode) };
            var sellerOptions = new List<SellerShippingOptions> { new("seller-1", "Seller One", options) };
            var selections = new Dictionary<string, string> { ["seller-1"] = "standard" };
            return new ShippingQuote(summary, sellerOptions, selections);
        }

        private static ShippingQuote BuildMultiSellerQuote()
        {
            var productOne = new ProductModel { Id = 1, Title = "Red Shirt", Price = 10, Stock = 5, SellerId = "seller-1" };
            var productTwo = new ProductModel { Id = 2, Title = "Blue Jeans", Price = 20, Stock = 5, SellerId = "seller-2" };
            var itemOne = new CartDisplayItem(productOne, 1, string.Empty, 10, 10, true, 5, new Dictionary<string, string>());
            var itemTwo = new CartDisplayItem(productTwo, 2, "32", 20, 40, true, 5, new Dictionary<string, string>());

            var sellerOneGroup = new CartSellerGroup("seller-1", "Seller One", 10, 7, 17, new List<CartDisplayItem> { itemOne });
            var sellerTwoGroup = new CartSellerGroup("seller-2", "Seller Two", 40, 5, 45, new List<CartDisplayItem> { itemTwo });
            var settlements = new CartSettlementSummary(
                new List<CartSellerSettlement>
                {
                    new("seller-1", 10, 7, 1, 16),
                    new("seller-2", 40, 5, 4, 41)
                },
                5,
                57);
            var summary = new CartSummary(new List<CartSellerGroup> { sellerOneGroup, sellerTwoGroup }, 50, 12, 62, 3, settlements);

            var sellerOneOptions = new List<ShippingMethodOption> { new("standard", "Standard", 5, "Standard", false), new("express", "Express", 7, "Express", true) };
            var sellerTwoOptions = new List<ShippingMethodOption> { new("standard", "Standard", 5, "Standard", true) };
            var sellerOptions = new List<SellerShippingOptions>
            {
                new("seller-1", "Seller One", sellerOneOptions),
                new("seller-2", "Seller Two", sellerTwoOptions)
            };
            var selections = new Dictionary<string, string> { ["seller-1"] = "express", ["seller-2"] = "standard" };

            return new ShippingQuote(summary, sellerOptions, selections);
        }

        private static ShippingQuote BuildMultiItemQuote(decimal discountTotal = 0)
        {
            var productOne = new ProductModel { Id = 1, Title = "Item One", Price = 12, Stock = 5, SellerId = "seller-1" };
            var productTwo = new ProductModel { Id = 2, Title = "Item Two", Price = 8, Stock = 5, SellerId = "seller-1" };
            var itemOne = new CartDisplayItem(productOne, 1, "Default", 12, 12, true, 5, new Dictionary<string, string>());
            var itemTwo = new CartDisplayItem(productTwo, 2, "Default", 8, 16, true, 5, new Dictionary<string, string>());

            var sellerGroup = new CartSellerGroup("seller-1", "Seller One", 28, 5, 33, new List<CartDisplayItem> { itemOne, itemTwo });
            var summary = new CartSummary(new List<CartSellerGroup> { sellerGroup }, 28, 5, Math.Max(0, 33 - discountTotal), 3, CartSettlementSummary.Empty, discountTotal);
            var options = new List<ShippingMethodOption> { new("standard", "Standard", 5, "Standard delivery", true) };
            var sellerOptions = new List<SellerShippingOptions> { new("seller-1", "Seller One", options) };
            var selections = new Dictionary<string, string> { ["seller-1"] = "standard" };

            return new ShippingQuote(summary, sellerOptions, selections);
        }

        private static ApplicationDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
