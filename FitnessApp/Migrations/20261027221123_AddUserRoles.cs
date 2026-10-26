using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessApp.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "dateofbirth",
                table: "customers",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "customers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddCheckConstraint(
                name: "customers_role_check",
                table: "customers",
                sql: "role IN ('Admin', 'User')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "customers_role_check",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "role",
                table: "customers");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "dateofbirth",
                table: "customers",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }
    }
}
