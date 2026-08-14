using GH_Net.Service.Abstractions;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace GH_Net.Service.Implements;

/// <summary>Builds a versioned request code from Windows installation and system volume IDs.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHardwareIdProvider : IHardwareIdProvider
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public string GetHardwareId()
    {
        string material = $"GHNET|HWID1|{ReadMachineGuid()}|{ReadSystemVolumeSerial()}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        string encoded = EncodeBase32(hash.AsSpan(0, 17));
        return "GHW1-" + string.Join('-', encoded.Chunk(5).Select(chunk => new string(chunk)));
    }

    private static string ReadMachineGuid()
    {
        using RegistryKey? key = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: false);
        return Convert.ToString(key?.GetValue("MachineGuid"))?
                   .Trim()
                   .ToUpperInvariant()
               ?? throw new InvalidOperationException("Windows MachineGuid is unavailable.");
    }

    private static string ReadSystemVolumeSerial()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        if (!GetVolumeInformation(
                root,
                null,
                0,
                out uint serial,
                out _,
                out _,
                null,
                0))
            return "NO-VOLUME-SERIAL";
        return serial.ToString("X8");
    }

    private static string EncodeBase32(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder((bytes.Length * 8 + 4) / 5);
        int buffer = 0;
        int bits = 0;
        foreach (byte value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                result.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
            result.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return result.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);
}
