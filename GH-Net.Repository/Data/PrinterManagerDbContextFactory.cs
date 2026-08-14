using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GH_Net.Repository.Data;

/// <summary>
/// Factory dùng cho EF Core CLI: dotnet ef migrations add / database update.
/// Chỉ dùng ở design-time — không inject vào DI runtime.
/// </summary>
public class PrinterManagerDbContextFactory : IDesignTimeDbContextFactory<PrinterManagerDbContext>
{
    public PrinterManagerDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<PrinterManagerDbContext>()
            .UseSqlite(PrinterManagerDbContext.DefaultConnectionString)
            .Options;
        return new PrinterManagerDbContext(opts);
    }
}
