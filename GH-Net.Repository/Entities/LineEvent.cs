namespace GH_Net.Repository.Entities;

/// <summary>Durable, non-replayed PLC command intent and transport outcome.</summary>
public sealed class LineEvent
{
    public long Id { get; set; }
    public int JobId { get; set; }
    public long? CameraSequence { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Outcome { get; set; } = "Pending";
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? SentAt { get; set; }
    public Job Job { get; set; } = null!;
}
