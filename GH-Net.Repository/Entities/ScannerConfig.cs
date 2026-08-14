namespace GH_Net.Repository.Entities;

public class ScannerConfig
{
    public int Id { get; set; }
    public string PortName { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public int Parity { get; set; }
    public int StopBits { get; set; } = 1;
    public string MessageTerminator { get; set; } = "CR";
    public int ReadTimeoutMs { get; set; } = 3000;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DateTime? LastConnectedAt { get; set; }
}
