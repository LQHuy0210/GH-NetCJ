using GH_Net.Core.Enums;

namespace GH_Net.Repository.Entities;

public class Product
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public int? CartonId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int SourceRowIndex { get; set; }
    public ProductStatus Status { get; set; } = ProductStatus.Available;
    public DateTime? ScannedAt { get; set; }
    public DateTime? PackedAt { get; set; }
    public Job Job { get; set; } = null!;
    public Carton? Carton { get; set; }
}
