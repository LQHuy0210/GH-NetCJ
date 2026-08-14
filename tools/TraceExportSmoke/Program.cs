using GH_Net.Core.Enums;
using GH_Net.Repository.Data;
using GH_Net.Repository.Entities;
using GH_Net.Service.Abstractions;
using GH_Net.Service.Implements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

bool keep = args.Length > 0;
string root = keep
    ? Path.GetFullPath(args[0])
    : Path.Combine(Path.GetTempPath(), $"ghnet-trace-export-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
string databasePath = Path.Combine(root, "test.db");
string connectionString = $"Data Source={databasePath}";

try
{
    await SeedAsync(connectionString);

    var factory = new UnitOfWorkFactory(connectionString);
    var trace = new TraceabilityService(factory);
    IReadOnlyList<TraceabilityResult> results = await trace.SearchExactAsync("SHARED-PRODUCT");
    Assert(results.Count == 2, "Same exact code must be grouped as one result per Job.");
    Assert(results.All(x => x.Product is not null && x.Carton?.Products.Count == 1 && x.Pallet?.Cartons.Count == 1),
        "Product trace must include full Carton/Pallet hierarchy.");
    IReadOnlyList<TraceabilityResult> palletResult = await trace.SearchExactAsync("PAL-1");
    Assert(palletResult.Single().Pallet?.Cartons.Single().Products.Single().Code == "SHARED-PRODUCT",
        "Pallet trace must expand down to Products.");

    var exporter = new PackingExportService(factory);
    string fullXlsx = Path.Combine(root, "full.xlsx");
    string fullZip = Path.Combine(root, "full.zip");
    await using (var file = File.Create(fullXlsx))
        await exporter.ExportAsync(1, new(PackingExportFormat.Excel), file);
    await using (var file = File.Create(fullZip))
        await exporter.ExportAsync(1, new(PackingExportFormat.CsvZip), file);

    using (var zip = ZipFile.OpenRead(fullZip))
    {
        string[] expected = ["Job.csv", "Products.csv", "Cartons.csv", "Pallets.csv", "ScanEvents.csv", "PrintAttempts.csv"];
        Assert(expected.All(name => zip.GetEntry(name) is not null), "CSV ZIP is missing full-Job datasets.");
        Assert(Read(zip, "Products.csv").Contains("SHARED-PRODUCT")
               && Read(zip, "PrintAttempts.csv").Contains("ZplSnapshot"),
            "CSV ZIP content is incomplete.");
    }

    using (var workbook = ZipFile.OpenRead(fullXlsx))
        Assert(workbook.GetEntry("xl/workbook.xml") is not null
               && Enumerable.Range(1, 6).All(i => workbook.GetEntry($"xl/worksheets/sheet{i}.xml") is not null),
            "Excel full-Job package is incomplete.");

    var unusedRequestXlsx = new PackingExportRequest(
        PackingExportFormat.Excel,
        PackingExportScope.UnusedCodes,
        PackingCodeSelection.Products | PackingCodeSelection.Pallets);
    var unusedRequestZip = unusedRequestXlsx with { Format = PackingExportFormat.CsvZip };
    string unusedXlsx = Path.Combine(root, "unused.xlsx");
    string unusedZip = Path.Combine(root, "unused.zip");
    PackingExportResult xlsxCounts;
    PackingExportResult zipCounts;
    await using (var file = File.Create(unusedXlsx))
        xlsxCounts = await exporter.ExportAsync(1, unusedRequestXlsx, file);
    await using (var file = File.Create(unusedZip))
        zipCounts = await exporter.ExportAsync(1, unusedRequestZip, file);

    Assert(xlsxCounts == zipCounts && zipCounts == new PackingExportResult(1, 0, 1, 0, 0),
        "Excel and CSV-ZIP unused-code counts must match the selected types.");

    using (var zip = ZipFile.OpenRead(unusedZip))
    {
        Assert(zip.Entries.Select(x => x.FullName).SequenceEqual(["UnusedProducts.csv", "UnusedPallets.csv"]),
            "Unused CSV ZIP must contain only the selected code types.");
        string products = Read(zip, "UnusedProducts.csv");
        string pallets = Read(zip, "UnusedPallets.csv");
        Assert(products.Contains("PRO-UNUSED") && !products.Contains("PRO-SCANNED") && !products.Contains("SHARED-PRODUCT"),
            "Unused Products must include only exact Available status.");
        Assert(pallets.Contains("PAL-UNUSED") && !pallets.Contains("PAL-1"),
            "Unused Pallets must include only exact Available status.");
        Assert(zip.GetEntry("UnusedCartons.csv") is null, "Unselected Cartons must not be exported.");
    }

    using (var workbook = ZipFile.OpenRead(unusedXlsx))
    {
        string workbookXml = Read(workbook, "xl/workbook.xml");
        Assert(workbookXml.Contains("UnusedProducts") && workbookXml.Contains("UnusedPallets")
               && !workbookXml.Contains("UnusedCartons"),
            "Excel must contain the same selected unused-code datasets as CSV ZIP.");
        string productsXml = Read(workbook, "xl/worksheets/sheet1.xml");
        string palletsXml = Read(workbook, "xl/worksheets/sheet2.xml");
        Assert(productsXml.Contains("PRO-UNUSED") && !productsXml.Contains("PRO-SCANNED"),
            "Excel unused Product filtering is incorrect.");
        Assert(palletsXml.Contains("PAL-UNUSED") && !palletsXml.Contains("PAL-1"),
            "Excel unused Pallet filtering is incorrect.");
    }

    await AssertThrowsAsync<InvalidOperationException>(async () =>
    {
        await using var output = new MemoryStream();
        await exporter.ExportAsync(1, new(
            PackingExportFormat.CsvZip,
            PackingExportScope.UnusedCodes,
            PackingCodeSelection.None), output);
    }, "Unused export without a selected code type must be rejected.");

    Console.WriteLine("Trace/export smoke passed: trace hierarchy, full export, and selectable Available-only export.");
}
finally
{
    SqliteConnection.ClearAllPools();
    if (!keep && Directory.Exists(root)) Directory.Delete(root, true);
}

static async Task SeedAsync(string connectionString)
{
    await using var database = PrinterManagerDbContext.Create(connectionString);
    await database.Database.MigrateAsync();
    for (int jobNumber = 1; jobNumber <= 2; jobNumber++)
    {
        var job = new Job
        {
            Name = $"Job {jobNumber}",
            ProductsPerCarton = 2,
            CartonsPerPallet = 2,
            TotalData = jobNumber == 1 ? 3 : 1
        };
        database.Jobs.Add(job);
        await database.SaveChangesAsync();

        var pallet = new Pallet
        {
            JobId = job.Id,
            Code = $"PAL-{jobNumber}",
            SourceRowIndex = 1,
            Sequence = 1,
            Status = PalletStatus.Filling,
            CartonCount = 1
        };
        database.Pallets.Add(pallet);
        await database.SaveChangesAsync();

        var carton = new Carton
        {
            JobId = job.Id,
            PalletId = pallet.Id,
            Code = $"CAR-{jobNumber}",
            SourceRowIndex = 1,
            Sequence = 1,
            Status = CartonStatus.Completed,
            ProductCount = 1
        };
        database.Cartons.Add(carton);
        await database.SaveChangesAsync();

        var product = new Product
        {
            JobId = job.Id,
            CartonId = carton.Id,
            Code = "SHARED-PRODUCT",
            SourceRowIndex = 1,
            Status = ProductStatus.Packed,
            ScannedAt = DateTime.Now,
            PackedAt = DateTime.Now
        };
        database.Products.Add(product);
        database.ScanEvents.Add(new ScanEvent
        {
            JobId = job.Id,
            ScannedCode = product.Code,
            ExpectedCodeType = ScanCodeType.Product,
            ResultCode = ScanResultCode.Accepted
        });
        database.PrintAttempts.Add(new PrintAttempt
        {
            JobId = job.Id,
            CartonId = carton.Id,
            AttemptNumber = 1,
            ZplSnapshot = "^XA^FDCAR^FS^XZ",
            Succeeded = true
        });

        if (jobNumber == 1)
        {
            database.Products.AddRange(
                new Product { JobId = job.Id, Code = "PRO-UNUSED", SourceRowIndex = 2, Status = ProductStatus.Available },
                new Product { JobId = job.Id, Code = "PRO-SCANNED", SourceRowIndex = 3, Status = ProductStatus.Scanned });
            database.Cartons.AddRange(
                new Carton { JobId = job.Id, Code = "CAR-UNUSED", SourceRowIndex = 2, Sequence = 2, Status = CartonStatus.Available },
                new Carton { JobId = job.Id, Code = "CAR-FILLING", SourceRowIndex = 3, Sequence = 3, Status = CartonStatus.Filling });
            database.Pallets.Add(new Pallet
            {
                JobId = job.Id,
                Code = "PAL-UNUSED",
                SourceRowIndex = 2,
                Sequence = 2,
                Status = PalletStatus.Available
            });
        }

        await database.SaveChangesAsync();
    }
}

static string Read(ZipArchive archive, string name)
{
    using var reader = new StreamReader(archive.GetEntry(name)!.Open());
    return reader.ReadToEnd();
}

static async Task AssertThrowsAsync<TException>(Func<Task> action, string message)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
