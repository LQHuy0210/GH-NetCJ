namespace GH_Net.Core.Enums;

public enum ScanResultCode
{
    Accepted = 0,
    Busy = 1,
    NotFound = 2,
    Duplicate = 3,
    AlreadyAssigned = 4,
    WrongCodeType = 5,
    UnexpectedCode = 6,
    NoCartonData = 7,
    NoPalletData = 8,
    PrintRequired = 9
}
