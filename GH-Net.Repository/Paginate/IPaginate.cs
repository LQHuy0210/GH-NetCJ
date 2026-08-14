namespace GH_Net.Repository.Paginate;

/// <summary>Kết quả phân trang — bao gồm metadata và danh sách items của trang hiện tại.</summary>
public interface IPaginate<TResult>
{
    /// <summary>Số item mỗi trang.</summary>
    int Size { get; }

    /// <summary>Trang hiện tại (bắt đầu từ 1).</summary>
    int Page { get; }

    /// <summary>Tổng số item (không phân trang).</summary>
    int Total { get; }

    /// <summary>Tổng số trang.</summary>
    int TotalPages { get; }

    /// <summary>Danh sách item của trang hiện tại.</summary>
    IReadOnlyList<TResult> Items { get; }

    /// <summary>Có trang trước không.</summary>
    bool HasPreviousPage { get; }

    /// <summary>Có trang tiếp theo không.</summary>
    bool HasNextPage { get; }
}
