namespace GH_Net.Licensing;

/// <summary>Independently enforceable GH-Net capabilities.</summary>
[Flags]
public enum LicenseFeature
{
    None = 0,
    Printer = 1,
    Scanner = 2,
    Export = 4,
    Plc = 8,
    All = Printer | Scanner | Export | Plc
}
