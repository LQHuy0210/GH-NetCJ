namespace GH_Net.Repository.Entities;
public class PrinterConfig
{public int Id{get;set;}public string Ip{get;set;}=string.Empty;public int Port{get;set;}=9100;public string? CartonZplTemplate{get;set;}public string? PalletZplTemplate{get;set;}public int ConnectTimeoutMs{get;set;}=3000;public int SendTimeoutMs{get;set;}=3000;public DateTime CreatedAt{get;set;}=DateTime.Now;public DateTime UpdatedAt{get;set;}=DateTime.Now;public DateTime? LastConnectedAt{get;set;}}
