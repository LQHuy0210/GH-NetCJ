using GH_Net.Repository.Abstractions;

namespace GH_Net.Repository.Data;

public sealed class TechnicalLockStore : ITechnicalLockStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GH-Net", "CJ", "plc-technical-access.bin");

    public async Task<byte[]?> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return null;
        if (new FileInfo(_path).Length > 4096) throw new InvalidDataException("Invalid technical access storage.");
        return await File.ReadAllBytesAsync(_path, ct);
    }

    public async Task WriteAsync(byte[] value, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, value, ct);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
