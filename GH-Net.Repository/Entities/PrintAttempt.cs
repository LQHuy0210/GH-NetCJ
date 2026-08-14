namespace GH_Net.Repository.Entities;

public class PrintAttempt
{
    public long Id { get; set; }
    public int JobId { get; set; }
    public int? CartonId { get; set; }
    public int? PalletId { get; set; }
    public int AttemptNumber { get; set; }
    public string ZplSnapshot { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public Job Job { get; set; } = null!;
    public Carton? Carton { get; set; }
    public Pallet? Pallet { get; set; }
}
