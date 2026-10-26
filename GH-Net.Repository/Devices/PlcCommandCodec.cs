using System.Globalization;
using GH_Net.Core.Enums;

namespace GH_Net.Repository.Devices;

/// <summary>Default DLM/R-Link commands, without transport framing.</summary>
public static class PlcCommandCodec
{
    public const string SynchronizationRequest = "PLC001";
    public const long MaximumIndex = 9_999_999;
    public const string DlmErrorCommand = "(R00001001000000100000000000000000000000000000000000000000000000000000000000000000000)";

    public static string Reject(PlcRejectMode mode, long index) => mode switch
    {
        PlcRejectMode.FixedCommand => "REJECT",
        PlcRejectMode.IndexedCommand => DlmErrorCommand + Digits(index, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static string Synchronize(long completed) => "0" + Digits(completed, 0);

    private static string Digits(long value, long minimum)
    {
        if (value < minimum || value > MaximumIndex) throw new ArgumentOutOfRangeException(nameof(value), "PLC index must fit seven digits.");
        return value.ToString("D7", CultureInfo.InvariantCulture);
    }
}
