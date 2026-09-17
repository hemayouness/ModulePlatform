using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulePlatform.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleRecordStatusAndSubmittedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ModuleRecords",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedBy",
                table: "ModuleRecords",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModuleRecords_Module_Collection_Status",
                table: "ModuleRecords",
                columns: new[] { "ModuleName", "Collection", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ModuleRecords_Module_Collection_SubmittedBy",
                table: "ModuleRecords",
                columns: new[] { "ModuleName", "Collection", "SubmittedBy" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModuleRecords_Module_Collection_Status",
                table: "ModuleRecords");

            migrationBuilder.DropIndex(
                name: "IX_ModuleRecords_Module_Collection_SubmittedBy",
                table: "ModuleRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ModuleRecords");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "ModuleRecords");
        }
    }
}
