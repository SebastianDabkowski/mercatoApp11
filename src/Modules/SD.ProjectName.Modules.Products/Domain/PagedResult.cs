using System;
using System.Collections.Generic;

namespace SD.ProjectName.Modules.Products.Domain
{
    public class PagedResult<T>
    {
        public required List<T> Items { get; init; }

        public int TotalCount { get; init; }

        public int PageNumber { get; init; }

        public int PageSize { get; init; }

        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

        public bool HasPreviousPage => TotalPages > 0 && PageNumber > 1;

        public bool HasNextPage => TotalPages > 0 && PageNumber < TotalPages;
    }
}
