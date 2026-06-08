using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddRevokeFieldsToVoucher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only add the new Revoke/Restore audit columns. (The AlterColumn timestamp
            // operations EF also scaffolded are pre-existing model drift unrelated to this
            // feature and would needlessly rewrite columns on a live DB, so they are omitted.)
            migrationBuilder.AddColumn<bool>(
                name: "IsRevoked",
                table: "Vouchers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedDate",
                table: "Vouchers",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedBy",
                table: "Vouchers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestoredDate",
                table: "Vouchers",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestoredBy",
                table: "Vouchers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsRevoked", table: "Vouchers");
            migrationBuilder.DropColumn(name: "RevokedDate", table: "Vouchers");
            migrationBuilder.DropColumn(name: "RevokedBy", table: "Vouchers");
            migrationBuilder.DropColumn(name: "RestoredDate", table: "Vouchers");
            migrationBuilder.DropColumn(name: "RestoredBy", table: "Vouchers");
        }
    }
}
