using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvancedCashVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdvancedPurchasingCustomerDetails",
                table: "Vouchers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AdvancedPurchasingCustomerId",
                table: "Vouchers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdvancedReceivingCustomerDetails",
                table: "Vouchers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AdvancedReceivingCustomerId",
                table: "Vouchers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_AdvancedPurchasingCustomerId",
                table: "Vouchers",
                column: "AdvancedPurchasingCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_AdvancedReceivingCustomerId",
                table: "Vouchers",
                column: "AdvancedReceivingCustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Vouchers_Customers_AdvancedPurchasingCustomerId",
                table: "Vouchers",
                column: "AdvancedPurchasingCustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vouchers_Customers_AdvancedReceivingCustomerId",
                table: "Vouchers",
                column: "AdvancedReceivingCustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vouchers_Customers_AdvancedPurchasingCustomerId",
                table: "Vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_Vouchers_Customers_AdvancedReceivingCustomerId",
                table: "Vouchers");

            migrationBuilder.DropIndex(
                name: "IX_Vouchers_AdvancedPurchasingCustomerId",
                table: "Vouchers");

            migrationBuilder.DropIndex(
                name: "IX_Vouchers_AdvancedReceivingCustomerId",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "AdvancedPurchasingCustomerDetails",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "AdvancedPurchasingCustomerId",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "AdvancedReceivingCustomerDetails",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "AdvancedReceivingCustomerId",
                table: "Vouchers");
        }
    }
}
