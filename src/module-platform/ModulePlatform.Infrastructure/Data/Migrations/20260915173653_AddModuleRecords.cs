using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulePlatform.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModuleRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Collection = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModuleRecords_Module_Collection",
                table: "ModuleRecords",
                columns: new[] { "ModuleName", "Collection" });

            migrationBuilder.CreateIndex(
                name: "UX_ModuleRecords_Module_Collection_ExternalId",
                table: "ModuleRecords",
                columns: new[] { "ModuleName", "Collection", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModuleRecords");
        }
    }
}
