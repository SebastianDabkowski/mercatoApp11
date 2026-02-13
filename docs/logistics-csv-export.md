# Logistics CSV export

Seller order exports are available from `Seller > Orders` via the `Export CSV` action. The download is UTF-8 with a header row and comma separators so it opens cleanly in Excel or Sheets.

Columns (newest orders first, max 5000 rows per export):
- `SubOrder` / `Order`: seller sub-order number and parent order number.
- `CreatedOn`: UTC timestamp of the order (`yyyy-MM-dd HH:mm:ssZ`).
- `Status`: normalized sub-order status.
- `BuyerName`, `BuyerEmail`, `BuyerPhone`.
- `Recipient`, `AddressLine1`, `AddressLine2`, `City`, `State`, `PostalCode`, `Country`.
- `ShippingMethod`, `ShippingCost`, `TrackingNumber`, `TrackingCarrier`.
- `Items`: aggregated as `Name (Variant) xQuantity`, joined with `|`.
- `TotalQuantity`, `GrandTotal` (decimal), `PaymentReference`.

Filters applied to the export mirror the order list (status, date range, buyer query, and the “Only orders without tracking” toggle). When no orders match the chosen filters, no file is generated and the page will display a message instead. Use narrower filters if you have more than 5000 matching orders to avoid truncation.
