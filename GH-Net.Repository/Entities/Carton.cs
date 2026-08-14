using GH_Net.Core.Enums;

namespace GH_Net.Repository.Entities;

public class Carton
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public int? PalletId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int SourceRowIndex { get; set; }
    public int Sequence { get; set; }
    public int ProductCount { get; set; }
    public int PrintCount { get; set; }
    public CartonStatus Status { get; set; } = CartonStatus.Available;
    public DateTime? FillingStartedAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Job Job { get; set; } = null!;
    public Pallet? Pallet { get; set; }
    public ICollection<Product> Products { get; set; } = [];
}
