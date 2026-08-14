using GH_Net.Core.Enums;
using GH_Net.Core.Packing;
using GH_Net.Repository.Data;
using GH_Net.Repository.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

Assert(!PackingPolicy.IsCartonFull(19, 20), "A carton below capacity must not be full.");
Assert(PackingPolicy.IsCartonFull(20, 20), "A carton at capacity must be full.");
Assert(!PackingPolicy.IsCartonFull(21, 20), "An over-capacity Carton must not pass the exact full rule.");
Assert(PackingPolicy.IsPalletFull(50, 50), "A pallet at capacity must be full.");
Assert(!PackingPolicy.IsPalletFull(51, 50), "An over-capacity Pallet must not pass the exact full rule.");
FullPackingPlan plan = PackingPolicy.BuildFullPackingPlan(40, 20, 4, 2, 2);
Assert(plan.RequiredCartons == 2 && plan.RequiredPallets == 1, "Full-unit capacity plan is incorrect.");
AssertThrows<InvalidOperationException>(() => PackingPolicy.BuildFullPackingPlan(30, 20, 2, 1, 2));
AssertThrows<InvalidOperationException>(() => PackingPolicy.BuildFullPackingPlan(40, 20, 2, 3, 1));
AssertThrows<InvalidOperationException>(() => PackingPolicy.BuildFullPackingPlan(40, 20, 1, 2, 1));
AssertThrows<InvalidOperationException>(() => PackingPolicy.BuildFullPackingPlan(40, 20, 2, 2, 0));
Assert(PackingPolicy.ExpectedCodeType(PackingWorkflowState.CartonFilling) == ScanCodeType.Product,
    "Carton filling must accept Product scans.");
Assert(PackingPolicy.ExpectedCodeType(PackingWorkflowState.CartonWaitingConfirmation) == ScanCodeType.Carton,
    "Carton confirmation must accept Carton scans.");
Assert(PackingPolicy.ExpectedCodeType(PackingWorkflowState.PalletWaitingConfirmation) == ScanCodeType.Pallet,
    "Pallet confirmation must accept Pallet scans.");
AssertThrows<InvalidOperationException>(() => PackingPolicy.ExpectedCodeType(PackingWorkflowState.Stopped));
AssertThrows<ArgumentOutOfRangeException>(() => PackingPolicy.IsCartonFull(0, 0));

string databasePath = Path.Combine(Path.GetTempPath(), $"ghnet-packing-domain-{Guid.NewGuid():N}.db");
try
{
    await using var context = PrinterManagerDbContext.Create($"Data Source={databasePath}");
    await context.Database.MigrateAsync();
    await AssertSchemaAsync(context);

    var job = new Job
    {
        Name = "Packing smoke",
        ProductsPerCarton = 20,
        CartonsPerPallet = 50,
        ProductCsvFileName = "products.csv",
        ProductCodeColumn = "PRODUCT_CODE",
        CartonCsvFileName = "cartons.csv",
        CartonCodeColumn = "CARTON_CODE",
        PalletCsvFileName = "pallets.csv",
        PalletCodeColumn = "PALLET_CODE",
        PackingWorkflowState = PackingWorkflowState.Stopped
    };
    context.Jobs.Add(job);
    await context.SaveChangesAsync();

    var pallet = new Pallet { JobId = job.Id, Code = "PALLET-001", SourceRowIndex = 0, Sequence = 1 };
    context.Pallets.Add(pallet);
    await context.SaveChangesAsync();
    var carton = new Carton { JobId = job.Id, PalletId = pallet.Id, Code = "CARTON-001", SourceRowIndex = 0, Sequence = 1 };
    context.Cartons.Add(carton);
    await context.SaveChangesAsync();
    context.Products.Add(new Product { JobId = job.Id, CartonId = carton.Id, Code = "PRODUCT-001", SourceRowIndex = 0 });
    context.ScanEvents.Add(new ScanEvent
    {
        JobId = job.Id,
        ScannedCode = "PRODUCT-001",
        ExpectedCodeType = ScanCodeType.Product,
        ResultCode = ScanResultCode.Accepted
    });
    context.PrintAttempts.Add(new PrintAttempt
    {
        JobId = job.Id,
        CartonId = carton.Id,
        AttemptNumber = 1,
        ZplSnapshot = "^XA^FDPRODUCT-001^FS^XZ",
        Succeeded = true
    });
    await context.SaveChangesAsync();

    context.Products.Add(new Product { JobId = job.Id, Code = "PRODUCT-001", SourceRowIndex = 1 });
    bool duplicateRejected = false;
    try { await context.SaveChangesAsync(); }
    catch (DbUpdateException) { duplicateRejected = true; context.ChangeTracker.Clear(); }
    Assert(duplicateRejected, "Duplicate Product codes inside one Job must be rejected.");

    Job storedJob = await context.Jobs.SingleAsync(item => item.Id == job.Id);
    context.Jobs.Remove(storedJob);
    await context.SaveChangesAsync();
    Assert(!await context.Products.AnyAsync(item => item.JobId == job.Id), "Deleting a Job must cascade to Products.");
    Assert(!await context.Cartons.AnyAsync(item => item.JobId == job.Id), "Deleting a Job must cascade to Cartons.");
    Assert(!await context.Pallets.AnyAsync(item => item.JobId == job.Id), "Deleting a Job must cascade to Pallets.");
    Assert(!await context.ScanEvents.AnyAsync(item => item.JobId == job.Id), "Deleting a Job must cascade to ScanEvents.");
    Assert(!await context.PrintAttempts.AnyAsync(item => item.JobId == job.Id), "Deleting a Job must cascade to PrintAttempts.");
}
finally
{
    SqliteConnection.ClearAllPools();
    if (File.Exists(databasePath)) File.Delete(databasePath);
}

Console.WriteLine("Packing domain smoke passed: full-unit policy/migration/schema/constraints/cascade.");

static async Task AssertSchemaAsync(PrinterManagerDbContext context)
{
    string[] requiredTables = ["Products", "Cartons", "Pallets", "ScanEvents", "PrintAttempts", "ScannerConfigs"];
    await context.Database.OpenConnectionAsync();
    var actualTables = new HashSet<string>(StringComparer.Ordinal);
    await using (var command = context.Database.GetDbConnection().CreateCommand())
    {
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) actualTables.Add(reader.GetString(0));
    }
    Assert(requiredTables.All(actualTables.Contains), $"Packing tables are missing. Actual: {string.Join(", ", actualTables.Order())}");
    string[] migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
    Assert(migrations.Length == 1 && migrations[0].EndsWith("_PackingInitial", StringComparison.Ordinal),
        $"Unexpected migration chain: {string.Join(", ", migrations)}");
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
