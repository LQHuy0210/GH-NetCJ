namespace GH_Net.Repository.Paginate;

/// <summary>Triển khai IPaginate — kết quả trả về từ GetPagingAsync.</summary>
public sealed class Paginate<TResult> : IPaginate<TResult>
{
    public int Size       { get; init; }
    public int Page       { get; init; }
    public int Total      { get; init; }
    public int TotalPages { get; init; }
    public IReadOnlyList<TResult> Items { get; init; }

    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage     => Page < TotalPages;

    public Paginate(IEnumerable<TResult> items, int total, int page, int size)
    {
        Items      = items is null ? Array.Empty<TResult>() : items.ToList().AsReadOnly();
        Total      = total;
        Page       = page < 1 ? 1 : page;
        Size       = size  < 1 ? 1 : size;
        TotalPages = size > 0 ? (int)Math.Ceiling(total / (double)size) : 0;
    }

    /// <summary>Tạo trang rỗng (không có dữ liệu).</summary>
    public static Paginate<TResult> Empty(int page = 1, int size = 20)
        => new([], 0, page, size);
}
