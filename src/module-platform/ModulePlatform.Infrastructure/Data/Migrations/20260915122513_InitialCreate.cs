using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulePlatform.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Modules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ActiveVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModuleVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SortMajor = table.Column<long>(type: "bigint", nullable: false),
                    SortMinor = table.Column<long>(type: "bigint", nullable: false),
                    SortPatch = table.Column<long>(type: "bigint", nullable: false),
                    EntryPath = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RoutesExposedModule = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StoragePrefix = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContractsVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    InstalledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InstalledBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModuleVersions_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Modules_ActiveVersionId",
                table: "Modules",
                column: "ActiveVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Modules_IsEnabled",
                table: "Modules",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_Modules_Name",
                table: "Modules",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModuleVersions_ContentHash",
                table: "ModuleVersions",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_ModuleVersions_ModuleId_SortMajor_SortMinor_SortPatch",
                table: "ModuleVersions",
                columns: new[] { "ModuleId", "SortMajor", "SortMinor", "SortPatch" });

            migrationBuilder.CreateIndex(
                name: "IX_ModuleVersions_ModuleId_Version",
                table: "ModuleVersions",
                columns: new[] { "ModuleId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ModuleVersions_OneActivePerModule",
                table: "ModuleVersions",
                column: "ModuleId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_Modules_ModuleVersions_ActiveVersionId",
                table: "Modules",
                column: "ActiveVersionId",
                principalTable: "ModuleVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Modules_ModuleVersions_ActiveVersionId",
                table: "Modules");

            migrationBuilder.DropTable(
                name: "ModuleVersions");

            migrationBuilder.DropTable(
                name: "Modules");
        }
    }
}
