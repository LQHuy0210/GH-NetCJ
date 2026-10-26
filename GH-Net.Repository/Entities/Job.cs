using GH_Net.Core.Enums;
namespace GH_Net.Repository.Entities;
public class Job
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalData { get; set; }
    public PackingMode Mode { get; set; }
    public bool UsePlc { get; set; }
    public long LastCameraSequence { get; set; }
    public JobStatus Status { get; set; } = JobStatus.NewlyCreated;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? StartedAt { get; set; }
    public int ProductsPerCarton { get; set; }
    public int CartonsPerPallet { get; set; }
    public string? ProductCsvFileName { get; set; }
    public string? ProductCodeColumn { get; set; }
    public string? CartonCsvFileName { get; set; }
    public string? CartonCodeColumn { get; set; }
    public string? PalletCsvFileName { get; set; }
    public string? PalletCodeColumn { get; set; }
    public PackingWorkflowState PackingWorkflowState { get; set; } = PackingWorkflowState.Stopped;
    public DateTime? StoppedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ICollection<Product> Products { get; set; } = [];
    public ICollection<Carton> Cartons { get; set; } = [];
    public ICollection<Pallet> Pallets { get; set; } = [];
}
