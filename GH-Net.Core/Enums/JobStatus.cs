namespace GH_Net.Core.Enums;

/// <summary>Trạng thái của một công việc in.</summary>
public enum JobStatus
{
    /// <summary>Đã tạo và có thể Start khi thiết bị toàn cục sẵn sàng.</summary>
    NewlyCreated = 0,

    /// <summary>Đang in (TCP kết nối, đang gửi data).</summary>
    Running = 2,

    /// <summary>Đã in hết toàn bộ dữ liệu.</summary>
    Completed = 3,

    /// <summary>Đã dừng tạm thời và có thể Start lại từ dữ liệu chưa hoàn tất.</summary>
    Paused = 4,
}
