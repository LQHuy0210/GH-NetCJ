namespace GH_Net.Service.Abstractions;

/// <summary>Produces the stable, non-reversible license request code for this machine.</summary>
public interface IHardwareIdProvider
{
    string GetHardwareId();
}
