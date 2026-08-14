namespace GH_Net.Core.Enums;

public enum PackingWorkflowState
{
    Stopped = 0,
    WaitingProduct = 1,
    CartonFilling = 2,
    CartonWaitingPrint = 3,
    CartonWaitingConfirmation = 4,
    PalletFilling = 5,
    PalletWaitingPrint = 6,
    PalletWaitingConfirmation = 7,
    Completed = 8,
    Faulted = 9
}
