using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulePlatform.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropModuleRecordSubmittedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModuleRecords_Module_Collection_SubmittedBy",
                table: "ModuleRecords");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "ModuleRecords");

            migrationBuilder.CreateIndex(
                name: "IX_ModuleRecords_Module_Collection_CreatedBy",
                table: "ModuleRecords",
                columns: new[] { "ModuleName", "Collection", "CreatedBy" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModuleRecords_Module_Collection_CreatedBy",
                table: "ModuleRecords");

            migrationBuilder.AddColumn<string>(
                name: "SubmittedBy",
                table: "ModuleRecords",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModuleRecords_Module_Collection_SubmittedBy",
                table: "ModuleRecords",
                columns: new[] { "ModuleName", "Collection", "SubmittedBy" });
        }
    }
}
