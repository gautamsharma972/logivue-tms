namespace LogiVue.Tms.Shared.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>Normalises paging input so every query has a bounded page size.</summary>
public static class Paging
{
    public const int MaxPageSize = 100;

    public static int NormalisePage(int page) => Math.Max(1, page);

    public static int NormalisePageSize(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);
}
