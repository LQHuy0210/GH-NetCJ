using GH_Net.Core.Enums;

namespace GH_Net.Repository.Entities;

public class ScanEvent
{
    public long Id { get; set; }
    public int JobId { get; set; }
    public string ScannedCode { get; set; } = string.Empty;
    public ScanCodeType ExpectedCodeType { get; set; }
    public ScanResultCode ResultCode { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public Job Job { get; set; } = null!;
}
