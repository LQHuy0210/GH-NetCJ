using GH_Net.Core.Enums;
using GH_Net.Repository.Data;
using GH_Net.Service.Abstractions;
using GH_Net.Service.Implements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

string root = Path.Combine(Path.GetTempPath(), $"ghnet-csv-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
string database = Path.Combine(root, "packing.db");
try
{
    string products = Path.Combine(root, "products.csv");
    string cartons = Path.Combine(root, "cartons.csv");
    string pallets = Path.Combine(root, "pallets.csv");
    await File.WriteAllTextAsync(products, "NAME,CODE\r\n\"Product, one\",P001\r\n\"Multi\nline\",P002\r\n");
    await File.WriteAllTextAsync(cartons, "CODE;NOTE\nC001;A\nC002;B\n");
    await File.WriteAllTextAsync(pallets, "CODE\tNOTE\nPL001\tA\n");

    var csv = new CsvImportService();
    CsvInspection inspection = await csv.InspectAsync(products);
    Assert(inspection.Headers.SequenceEqual(["NAME", "CODE"]), "CSV headers were not detected.");
    Assert(inspection.DataRowCount == 2 && inspection.PreviewRows[1].SourceLineNumber == 3,
        "Quoted multiline CSV row was not preserved.");
    Assert((await csv.ReadCodeColumnAsync(products, "CODE")).Select(item => item.Code).SequenceEqual(["P001", "P002"]),
        "Selected Product code column is incorrect.");
    Assert((await csv.InspectAsync(cartons)).Delimiter == ';', "Semicolon delimiter was not detected.");
    Assert((await csv.InspectAsync(pallets)).Delimiter == '\t', "TAB delimiter was not detected.");

    await using (var context = PrinterManagerDbContext.Create($"Data Source={database}")) await context.Database.MigrateAsync();
    var service = new PackingJobService(new UnitOfWorkFactory($"Data Source={database}"), csv);
    var job = await service.CreateAsync(new CreatePackingJobRequest
    {
        UseAutomaticName = false, Name = "CSV packing", ProductsPerCarton = 2, CartonsPerPallet = 1,
        Products = new PackingCsvSelection(products, "CODE"),
        Cartons = new PackingCsvSelection(cartons, "CODE"),
        Pallets = new PackingCsvSelection(pallets, "CODE")
    });
    await using (var context = PrinterManagerDbContext.Create($"Data Source={database}"))
    {
        var stored = await context.Jobs.SingleAsync(item => item.Id == job.Id);
        Assert(stored.ProductsPerCarton == 2 && stored.CartonsPerPallet == 1,
            "Packing Job metadata is incorrect.");
        Assert(await context.Products.CountAsync() == 2 && await context.Cartons.CountAsync() == 2 && await context.Pallets.CountAsync() == 1,
            "CSV rows were not persisted atomically.");
        Assert((await context.Cartons.OrderBy(item => item.Sequence).Select(item => item.Code).ToArrayAsync()).SequenceEqual(["C001", "C002"]),
            "Carton CSV order was not preserved.");
    }

    await AssertThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new CreatePackingJobRequest
    {
        UseAutomaticName = false, Name = "Partial carton rejected", ProductsPerCarton = 3, CartonsPerPallet = 1,
        Products = new PackingCsvSelection(products, "CODE"),
        Cartons = new PackingCsvSelection(cartons, "CODE"),
        Pallets = new PackingCsvSelection(pallets, "CODE")
    }));
    await AssertThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new CreatePackingJobRequest
    {
        UseAutomaticName = false, Name = "Pallet shortage rejected", ProductsPerCarton = 1, CartonsPerPallet = 1,
        Products = new PackingCsvSelection(products, "CODE"),
        Cartons = new PackingCsvSelection(cartons, "CODE"),
        Pallets = new PackingCsvSelection(pallets, "CODE")
    }));
    await using (var context = PrinterManagerDbContext.Create($"Data Source={database}"))
        Assert(await context.Jobs.CountAsync() == 1,
            "Invalid full-unit plans must be rejected before any Job data is persisted.");

    string duplicates = Path.Combine(root, "duplicates.csv");
    await File.WriteAllTextAsync(duplicates, "CODE\nDUP\nDUP\n");
    await AssertThrowsAsync<InvalidOperationException>(() => csv.ReadCodeColumnAsync(duplicates, "CODE"));
    string blank = Path.Combine(root, "blank.csv");
    await File.WriteAllTextAsync(blank, "CODE,NOTE\n,missing\n");
    await AssertThrowsAsync<InvalidOperationException>(() => csv.ReadCodeColumnAsync(blank, "CODE"));
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

Console.WriteLine("CSV import smoke passed: detection/quotes/validation/order/atomic Job creation.");

static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
{
    try { await action(); } catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
