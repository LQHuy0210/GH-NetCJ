using GH_Net.Core.Enums;

namespace GH_Net.Core.Packing;

public sealed record FullPackingPlan(int ProductCount, int RequiredCartons, int RequiredPallets);

/// <summary>Pure packing rules shared by services, UI validation and smoke tests.</summary>
public static class PackingPolicy
{
    public static bool IsCartonFull(int productCount, int productsPerCarton)
    {
        ValidateCount(productCount, nameof(productCount));
        ValidateCapacity(productsPerCarton, nameof(productsPerCarton));
        return productCount == productsPerCarton;
    }

    public static bool IsPalletFull(int cartonCount, int cartonsPerPallet)
    {
        ValidateCount(cartonCount, nameof(cartonCount));
        ValidateCapacity(cartonsPerPallet, nameof(cartonsPerPallet));
        return cartonCount == cartonsPerPallet;
    }

    public static FullPackingPlan BuildFullPackingPlan(
        int productCount,
        int productsPerCarton,
        int cartonCodeCount,
        int cartonsPerPallet,
        int palletCodeCount)
    {
        ValidateCount(productCount, nameof(productCount));
        ValidateCapacity(productsPerCarton, nameof(productsPerCarton));
        ValidateCount(cartonCodeCount, nameof(cartonCodeCount));
        ValidateCapacity(cartonsPerPallet, nameof(cartonsPerPallet));
        ValidateCount(palletCodeCount, nameof(palletCodeCount));
        if (productCount == 0)
            throw new InvalidOperationException("At least one Product code is required.");
        if (productCount % productsPerCarton != 0)
            throw new InvalidOperationException(
                $"Product count ({productCount}) must be divisible by Products per Carton ({productsPerCarton}); partial Cartons cannot be printed.");

        int requiredCartons = productCount / productsPerCarton;
        if (requiredCartons % cartonsPerPallet != 0)
            throw new InvalidOperationException(
                $"Required Carton count ({requiredCartons}) must be divisible by Cartons per Pallet ({cartonsPerPallet}); partial Pallets cannot be printed.");

        int requiredPallets = requiredCartons / cartonsPerPallet;
        if (cartonCodeCount < requiredCartons)
            throw new InvalidOperationException(
                $"Not enough Carton codes: {cartonCodeCount} available, {requiredCartons} required.");
        if (palletCodeCount < requiredPallets)
            throw new InvalidOperationException(
                $"Not enough Pallet codes: {palletCodeCount} available, {requiredPallets} required.");

        return new FullPackingPlan(productCount, requiredCartons, requiredPallets);
    }

    public static ScanCodeType ExpectedCodeType(PackingWorkflowState state) => state switch
    {
        PackingWorkflowState.WaitingProduct or
        PackingWorkflowState.CartonFilling or
        PackingWorkflowState.PalletFilling => ScanCodeType.Product,
        PackingWorkflowState.CartonWaitingConfirmation => ScanCodeType.Carton,
        PackingWorkflowState.PalletWaitingConfirmation => ScanCodeType.Pallet,
        _ => throw new InvalidOperationException($"Workflow state {state} does not accept scanner input.")
    };

    public static void ValidateCapacity(int value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, "Capacity must be greater than zero.");
    }

    private static void ValidateCount(int value, string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName, "Count cannot be negative.");
    }
}
