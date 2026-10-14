namespace GH_Net.Licensing;

/// <summary>Independently enforceable GH-Net capabilities.</summary>
[Flags]
public enum LicenseFeature
{
    None = 0,
    Printer = 1,
    Camera = 2,
    Export = 4,
    Plc = 8,
    Scanner = 16,
    All = Printer | Camera | Export | Plc | Scanner
}
