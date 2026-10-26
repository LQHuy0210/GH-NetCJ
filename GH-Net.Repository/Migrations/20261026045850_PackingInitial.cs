using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GH_Net.Repository.Migrations
{
    /// <inheritdoc />
    public partial class PackingInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CameraIp = table.Column<string>(type: "TEXT", nullable: false),
                    CameraPort = table.Column<int>(type: "INTEGER", nullable: false),
                    CameraTerminator = table.Column<string>(type: "TEXT", nullable: false),
                    NoReadToken = table.Column<string>(type: "TEXT", nullable: false),
                    PlcIp = table.Column<string>(type: "TEXT", nullable: false),
                    PlcPort = table.Column<int>(type: "INTEGER", nullable: false),
                    Framing = table.Column<int>(type: "INTEGER", nullable: false),
                    RejectMode = table.Column<int>(type: "INTEGER", nullable: false),
                    ReadyCommand = table.Column<string>(type: "TEXT", nullable: false),
                    HoldCommand = table.Column<string>(type: "TEXT", nullable: false),
                    FullCommand = table.Column<string>(type: "TEXT", nullable: false),
                    FaultCommand = table.Column<string>(type: "TEXT", nullable: false),
                    StopCommand = table.Column<string>(type: "TEXT", nullable: false),
                    TimeoutMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Sensors = table.Column<string>(type: "TEXT", nullable: false,
                        defaultValue: "[{\"Number\":1,\"DelayDistance\":0,\"IgnoreDistance\":0,\"PulseCount\":3600,\"EncoderDiameter\":51,\"Gap\":0,\"ErrorCondition\":0},{\"Number\":2,\"DelayDistance\":0,\"IgnoreDistance\":0,\"PulseCount\":3600,\"EncoderDiameter\":51,\"Gap\":0,\"ErrorCondition\":0}]")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TotalData = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    UsePlc = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastCameraSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProductsPerCarton = table.Column<int>(type: "INTEGER", nullable: false),
                    CartonsPerPallet = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductCsvFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    ProductCodeColumn = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CartonCsvFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    CartonCodeColumn = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PalletCsvFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    PalletCodeColumn = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PackingWorkflowState = table.Column<int>(type: "INTEGER", nullable: false),
                    StoppedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                    table.CheckConstraint("CK_Jobs_Capacity", "ProductsPerCarton > 0 AND CartonsPerPallet > 0");
                });

            migrationBuilder.CreateTable(
                name: "PrinterConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ip = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    CartonZplTemplate = table.Column<string>(type: "TEXT", nullable: true),
                    PalletZplTemplate = table.Column<string>(type: "TEXT", nullable: true),
                    ConnectTimeoutMs = table.Column<int>(type: "INTEGER", nullable: false),
                    SendTimeoutMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScannerConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PortName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    BaudRate = table.Column<int>(type: "INTEGER", nullable: false),
                    DataBits = table.Column<int>(type: "INTEGER", nullable: false),
                    Parity = table.Column<int>(type: "INTEGER", nullable: false),
                    StopBits = table.Column<int>(type: "INTEGER", nullable: false),
                    MessageTerminator = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ReadTimeoutMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScannerConfigs", x => x.Id);
                    table.CheckConstraint("CK_Scanner_Baud", "BaudRate > 0");
                    table.CheckConstraint("CK_Scanner_DataBits", "DataBits >= 5 AND DataBits <= 8");
                    table.CheckConstraint("CK_Scanner_Timeout", "ReadTimeoutMs > 0");
                });

            migrationBuilder.CreateTable(
                name: "LineEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    CameraSequence = table.Column<long>(type: "INTEGER", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineEvents_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pallets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SourceRowIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    CartonCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PrintCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FillingStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PrintedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pallets", x => x.Id);
                    table.CheckConstraint("CK_Pallets_Counts", "CartonCount >= 0 AND PrintCount >= 0");
                    table.CheckConstraint("CK_Pallets_Row", "SourceRowIndex >= 0");
                    table.ForeignKey(
                        name: "FK_Pallets_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScanEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    CameraSequence = table.Column<long>(type: "INTEGER", nullable: true),
                    ScannedCode = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ExpectedCodeType = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanEvents_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Cartons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    PalletId = table.Column<int>(type: "INTEGER", nullable: true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SourceRowIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PrintCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FillingStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PrintedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cartons", x => x.Id);
                    table.CheckConstraint("CK_Cartons_Counts", "ProductCount >= 0 AND PrintCount >= 0");
                    table.CheckConstraint("CK_Cartons_Row", "SourceRowIndex >= 0");
                    table.ForeignKey(
                        name: "FK_Cartons_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Cartons_Pallets_PalletId",
                        column: x => x.PalletId,
                        principalTable: "Pallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrintAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    CartonId = table.Column<int>(type: "INTEGER", nullable: true),
                    PalletId = table.Column<int>(type: "INTEGER", nullable: true),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ZplSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    Succeeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintAttempts", x => x.Id);
                    table.CheckConstraint("CK_PrintAttempts_Number", "AttemptNumber > 0");
                    table.CheckConstraint("CK_PrintAttempts_Target", "(CartonId IS NOT NULL AND PalletId IS NULL) OR (CartonId IS NULL AND PalletId IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PrintAttempts_Cartons_CartonId",
                        column: x => x.CartonId,
                        principalTable: "Cartons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrintAttempts_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrintAttempts_Pallets_PalletId",
                        column: x => x.PalletId,
                        principalTable: "Pallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    CartonId = table.Column<int>(type: "INTEGER", nullable: true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SourceRowIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PackedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Products_Row", "SourceRowIndex >= 0");
                    table.ForeignKey(
                        name: "FK_Products_Cartons_CartonId",
                        column: x => x.CartonId,
                        principalTable: "Cartons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cartons_JobId_Code",
                table: "Cartons",
                columns: new[] { "JobId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cartons_JobId_SourceRowIndex",
                table: "Cartons",
                columns: new[] { "JobId", "SourceRowIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cartons_JobId_Status",
                table: "Cartons",
                columns: new[] { "JobId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Cartons_PalletId",
                table: "Cartons",
                column: "PalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CreatedAt",
                table: "Jobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Status",
                table: "Jobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_LineEvents_JobId_CreatedAt",
                table: "LineEvents",
                columns: new[] { "JobId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Pallets_JobId_Code",
                table: "Pallets",
                columns: new[] { "JobId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pallets_JobId_SourceRowIndex",
                table: "Pallets",
                columns: new[] { "JobId", "SourceRowIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pallets_JobId_Status",
                table: "Pallets",
                columns: new[] { "JobId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PrintAttempts_CartonId",
                table: "PrintAttempts",
                column: "CartonId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintAttempts_JobId_CreatedAt",
                table: "PrintAttempts",
                columns: new[] { "JobId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PrintAttempts_PalletId",
                table: "PrintAttempts",
                column: "PalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CartonId",
                table: "Products",
                column: "CartonId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_JobId_Code",
                table: "Products",
                columns: new[] { "JobId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_JobId_Status",
                table: "Products",
                columns: new[] { "JobId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScanEvents_JobId_CreatedAt",
                table: "ScanEvents",
                columns: new[] { "JobId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationConfigs");

            migrationBuilder.DropTable(
                name: "LineEvents");

            migrationBuilder.DropTable(
                name: "PrintAttempts");

            migrationBuilder.DropTable(
                name: "PrinterConfigs");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "ScanEvents");

            migrationBuilder.DropTable(
                name: "ScannerConfigs");

            migrationBuilder.DropTable(
                name: "Cartons");

            migrationBuilder.DropTable(
                name: "Pallets");

            migrationBuilder.DropTable(
                name: "Jobs");
        }
    }
}
