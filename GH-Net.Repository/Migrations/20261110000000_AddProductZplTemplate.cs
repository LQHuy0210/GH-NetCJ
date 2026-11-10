using GH_Net.Repository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GH_Net.Repository.Migrations;

[DbContext(typeof(PrinterManagerDbContext))]
[Migration("20261110000000_AddProductZplTemplate")]
public sealed class AddProductZplTemplate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProductZplTemplate",
            table: "PrinterConfigs",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ProductZplTemplate",
            table: "PrinterConfigs");
    }
}
