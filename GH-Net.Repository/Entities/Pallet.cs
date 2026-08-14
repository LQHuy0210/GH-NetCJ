using GH_Net.Core.Enums;

namespace GH_Net.Repository.Entities;

public class Pallet
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int SourceRowIndex { get; set; }
    public int Sequence { get; set; }
    public int CartonCount { get; set; }
    public int PrintCount { get; set; }
    public PalletStatus Status { get; set; } = PalletStatus.Available;
    public DateTime? FillingStartedAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Job Job { get; set; } = null!;
    public ICollection<Carton> Cartons { get; set; } = [];
}
