namespace GH_Net.Repository.Abstractions;

/// <summary>Opaque local credential envelope; never contains a plaintext access code.</summary>
public interface ITechnicalLockStore
{
    Task<byte[]?> ReadAsync(CancellationToken ct = default);
    Task WriteAsync(byte[] value, CancellationToken ct = default);
}
