using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddThemeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockMode",
                table: "PageLocks",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ThemeSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ThemeMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PrimaryColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    SecondaryColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    SuccessColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    DangerColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    WarningColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    InfoColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    BackgroundColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    TextColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    CardBackgroundColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    NavbarBackgroundColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    SidebarBackgroundColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    FooterBackgroundColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThemeSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThemeSettings");

            migrationBuilder.DropColumn(
                name: "LockMode",
                table: "PageLocks");
        }
    }
}
