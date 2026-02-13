namespace SD.ProjectName.WebApp.Data
{
    public class AnalyticsEvent
    {
        public int Id { get; set; }

        public string EventType { get; set; } = string.Empty;

        public DateTimeOffset OccurredOn { get; set; }

        public string? UserId { get; set; }

        public string? SessionId { get; set; }

        public int? ProductId { get; set; }

        public string? SellerId { get; set; }

        public int? OrderId { get; set; }

        public string? Keyword { get; set; }

        public int? Quantity { get; set; }

        public decimal? Amount { get; set; }

        public string? MetadataJson { get; set; }
    }
}
