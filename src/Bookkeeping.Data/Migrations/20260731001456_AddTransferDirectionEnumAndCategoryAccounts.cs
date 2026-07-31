using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookkeeping.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferDirectionEnumAndCategoryAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DonationCategories_Accounts_IncomeAccountId",
                table: "DonationCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseCategories_Accounts_ExpenseAccountId",
                table: "ExpenseCategories");

            migrationBuilder.AddForeignKey(
                name: "FK_DonationCategories_Accounts_IncomeAccountId",
                table: "DonationCategories",
                column: "IncomeAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseCategories_Accounts_ExpenseAccountId",
                table: "ExpenseCategories",
                column: "ExpenseAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DonationCategories_Accounts_IncomeAccountId",
                table: "DonationCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseCategories_Accounts_ExpenseAccountId",
                table: "ExpenseCategories");

            migrationBuilder.AddForeignKey(
                name: "FK_DonationCategories_Accounts_IncomeAccountId",
                table: "DonationCategories",
                column: "IncomeAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseCategories_Accounts_ExpenseAccountId",
                table: "ExpenseCategories",
                column: "ExpenseAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");
        }
    }
}
