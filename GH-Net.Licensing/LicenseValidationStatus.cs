namespace GH_Net.Licensing;

/// <summary>Machine-readable result of validating a license token.</summary>
public enum LicenseValidationStatus
{
    Valid,
    Missing,
    Malformed,
    UnknownKey,
    InvalidSignature,
    WrongProduct,
    HardwareMismatch,
    NotYetValid,
    Expired,
    ClockRollback,
    StorageError
}
