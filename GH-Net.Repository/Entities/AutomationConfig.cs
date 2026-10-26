using GH_Net.Core.Enums;

namespace GH_Net.Repository.Entities;

/// <summary>One global camera/PLC connection profile. Commands exclude transport framing.</summary>
public sealed class AutomationConfig
{
    public int Id { get; set; } = 1;
    public string CameraIp { get; set; } = "127.0.0.1";
    public int CameraPort { get; set; } = 9001;
    public string CameraTerminator { get; set; } = "CR";
    public string NoReadToken { get; set; } = "NOREAD";
    public string PlcIp { get; set; } = "127.0.0.1";
    public int PlcPort { get; set; } = 9002;
    public PlcFraming Framing { get; set; } = PlcFraming.CRLF;
    public PlcRejectMode RejectMode { get; set; }
    public string ReadyCommand { get; set; } = "READY";
    public string HoldCommand { get; set; } = "HOLD";
    public string FullCommand { get; set; } = "FULL";
    public string FaultCommand { get; set; } = "FAULT";
    public string StopCommand { get; set; } = "STOP";
    public int TimeoutMs { get; set; } = 3000;
    public List<PlcSensorSettings> Sensors { get; set; } = PlcSensorSettings.Defaults();
}

/// <summary>Stored PLC encoder/sensor tuning, matching GHNet-ZRP fields.</summary>
public sealed class PlcSensorSettings
{
    public static List<PlcSensorSettings> Defaults() =>
    [
        new() { Number = 1, PulseCount = 3600, EncoderDiameter = 51m },
        new() { Number = 2, PulseCount = 3600, EncoderDiameter = 51m }
    ];
    public int Number { get; set; }
    public int DelayDistance { get; set; }
    public int IgnoreDistance { get; set; }
    public int PulseCount { get; set; } = 3600;
    public decimal EncoderDiameter { get; set; } = 51m;
    public int Gap { get; set; }
    public int ErrorCondition { get; set; }
}
