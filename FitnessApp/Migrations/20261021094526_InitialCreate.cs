using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FitnessApp.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    customerid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fullname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Password = table.Column<string>(type: "text", nullable: false),
                    dateofbirth = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("customers_pkey", x => x.customerid);
                });

            migrationBuilder.CreateTable(
                name: "gympasses",
                columns: table => new
                {
                    passtypeid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    passname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    price = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    maxvisits = table.Column<int>(type: "integer", nullable: false),
                    validitydays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("gympasses_pkey", x => x.passtypeid);
                });

            migrationBuilder.CreateTable(
                name: "trainers",
                columns: table => new
                {
                    trainerid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fullname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    specialty = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("trainers_pkey", x => x.trainerid);
                });

            migrationBuilder.CreateTable(
                name: "refreshtokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<int>(type: "integer", nullable: false),
                    token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    expiresat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("refreshtokens_pkey", x => x.id);
                    table.ForeignKey(
                        name: "fk_refreshtokens_customers",
                        column: x => x.userid,
                        principalTable: "customers",
                        principalColumn: "customerid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customerpasses",
                columns: table => new
                {
                    customerpassid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customerid = table.Column<int>(type: "integer", nullable: false),
                    passtypeid = table.Column<int>(type: "integer", nullable: false),
                    purchasedate = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "CURRENT_DATE"),
                    expirydate = table.Column<DateOnly>(type: "date", nullable: false),
                    remainingvisits = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("customerpasses_pkey", x => x.customerpassid);
                    table.ForeignKey(
                        name: "fk_customerpasses_customers",
                        column: x => x.customerid,
                        principalTable: "customers",
                        principalColumn: "customerid");
                    table.ForeignKey(
                        name: "fk_customerpasses_gympasses",
                        column: x => x.passtypeid,
                        principalTable: "gympasses",
                        principalColumn: "passtypeid");
                });

            migrationBuilder.CreateTable(
                name: "checkins",
                columns: table => new
                {
                    checkinid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customerpassid = table.Column<int>(type: "integer", nullable: false),
                    checkindatetime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    trainerid = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("checkins_pkey", x => x.checkinid);
                    table.ForeignKey(
                        name: "fk_checkins_customerpasses",
                        column: x => x.customerpassid,
                        principalTable: "customerpasses",
                        principalColumn: "customerpassid");
                    table.ForeignKey(
                        name: "fk_checkins_trainers",
                        column: x => x.trainerid,
                        principalTable: "trainers",
                        principalColumn: "trainerid");
                });

            migrationBuilder.CreateIndex(
                name: "IX_checkins_customerpassid",
                table: "checkins",
                column: "customerpassid");

            migrationBuilder.CreateIndex(
                name: "IX_checkins_trainerid",
                table: "checkins",
                column: "trainerid");

            migrationBuilder.CreateIndex(
                name: "IX_customerpasses_customerid",
                table: "customerpasses",
                column: "customerid");

            migrationBuilder.CreateIndex(
                name: "IX_customerpasses_passtypeid",
                table: "customerpasses",
                column: "passtypeid");

            migrationBuilder.CreateIndex(
                name: "customers_phone_key",
                table: "customers",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "gympasses_passname_key",
                table: "gympasses",
                column: "passname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refreshtokens_token",
                table: "refreshtokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refreshtokens_userid",
                table: "refreshtokens",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "trainers_phone_key",
                table: "trainers",
                column: "phone",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkins");

            migrationBuilder.DropTable(
                name: "refreshtokens");

            migrationBuilder.DropTable(
                name: "customerpasses");

            migrationBuilder.DropTable(
                name: "trainers");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "gympasses");
        }
    }
}
