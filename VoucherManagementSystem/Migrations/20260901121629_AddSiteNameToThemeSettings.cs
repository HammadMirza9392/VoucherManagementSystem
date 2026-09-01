using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteNameToThemeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SiteName",
                table: "ThemeSettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SiteShortName",
                table: "ThemeSettings",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Existing rows get an empty string from the AddColumn default — backfill them
            // with the current site name so Theme Settings opens with the real value.
            migrationBuilder.Sql(
                "UPDATE \"ThemeSettings\" " +
                "SET \"SiteName\" = 'AL-Hafiz Voucher System', \"SiteShortName\" = 'AL-Hafiz' " +
                "WHERE \"SiteName\" IS NULL OR \"SiteName\" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SiteName",
                table: "ThemeSettings");

            migrationBuilder.DropColumn(
                name: "SiteShortName",
                table: "ThemeSettings");
        }
    }
}
