using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparateSeatTypesFromServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_seat_types_waterbus_services_WaterbusServiceId",
                table: "seat_types");

            migrationBuilder.DropIndex(
                name: "IX_seat_types_WaterbusServiceId_Code",
                table: "seat_types");

            migrationBuilder.DropIndex(
                name: "IX_seat_types_WaterbusServiceId_DisplayOrder",
                table: "seat_types");

            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE seat_type_migration_map ON COMMIT DROP AS
                SELECT
                    source."Id" AS old_seat_type_id,
                    source."WaterbusServiceId" AS service_id,
                    CASE
                        WHEN UPPER(source."Code") = 'VIP' THEN 'VIP'
                        ELSE 'STANDARD'
                    END AS seat_type_code,
                    (
                        SELECT canonical."Id"
                        FROM seat_types canonical
                        WHERE UPPER(canonical."Code") = CASE
                            WHEN UPPER(source."Code") = 'VIP' THEN 'VIP'
                            ELSE 'STANDARD'
                        END
                        ORDER BY canonical."DisplayOrder", canonical."Id"
                        LIMIT 1
                    ) AS canonical_seat_type_id
                FROM seat_types source;

                UPDATE seats seat
                SET "SeatTypeId" = map.canonical_seat_type_id
                FROM seat_type_migration_map map
                WHERE seat."SeatTypeId" = map.old_seat_type_id
                  AND map.old_seat_type_id <> map.canonical_seat_type_id;

                DELETE FROM seat_types seat_type
                USING seat_type_migration_map map
                WHERE seat_type."Id" = map.old_seat_type_id
                  AND map.old_seat_type_id <> map.canonical_seat_type_id;

                UPDATE seat_types
                SET "Code" = UPPER("Code");
                """);

            migrationBuilder.DropColumn(
                name: "WaterbusServiceId",
                table: "seat_types");

            migrationBuilder.AddColumn<string>(
                name: "SeatSetupType",
                table: "vessels",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "FullStandard");

            migrationBuilder.CreateTable(
                name: "service_seat_type_prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WaterbusServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceModifier = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 1m),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_seat_type_prices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_seat_type_prices_seat_types_SeatTypeId",
                        column: x => x.SeatTypeId,
                        principalTable: "seat_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_seat_type_prices_waterbus_services_WaterbusServiceId",
                        column: x => x.WaterbusServiceId,
                        principalTable: "waterbus_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO service_seat_type_prices (
                    "Id",
                    "WaterbusServiceId",
                    "SeatTypeId",
                    "PriceModifier",
                    "IsActive",
                    "Created",
                    "CreatedBy",
                    "LastModified",
                    "LastModifiedBy")
                SELECT DISTINCT
                    MD5(map.service_id::text || map.canonical_seat_type_id::text)::uuid,
                    map.service_id,
                    map.canonical_seat_type_id,
                    CASE WHEN map.seat_type_code = 'VIP' THEN 1.5 ELSE 1.0 END,
                    TRUE,
                    CURRENT_TIMESTAMP,
                    NULL,
                    CURRENT_TIMESTAMP,
                    NULL
                FROM seat_type_migration_map map;

                INSERT INTO service_seat_type_prices (
                    "Id",
                    "WaterbusServiceId",
                    "SeatTypeId",
                    "PriceModifier",
                    "IsActive",
                    "Created",
                    "CreatedBy",
                    "LastModified",
                    "LastModifiedBy")
                SELECT
                    MD5(service."Id"::text || seat_type."Id"::text)::uuid,
                    service."Id",
                    seat_type."Id",
                    1.0,
                    TRUE,
                    CURRENT_TIMESTAMP,
                    NULL,
                    CURRENT_TIMESTAMP,
                    NULL
                FROM waterbus_services service
                CROSS JOIN seat_types seat_type
                WHERE service."Code" = 'WT'
                  AND seat_type."Code" = 'VIP'
                ON CONFLICT ("Id") DO UPDATE
                SET "PriceModifier" = 1.0,
                    "IsActive" = TRUE,
                    "LastModified" = CURRENT_TIMESTAMP;

                UPDATE vessels vessel
                SET "SeatSetupType" = 'StandardAndVip'
                WHERE EXISTS (
                    SELECT 1
                    FROM seat_type_migration_map map
                    WHERE map.service_id = vessel."WaterbusServiceId"
                      AND map.seat_type_code = 'VIP'
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_seat_types_Code",
                table: "seat_types",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seat_types_DisplayOrder",
                table: "seat_types",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_service_seat_type_prices_SeatTypeId",
                table: "service_seat_type_prices",
                column: "SeatTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_service_seat_type_prices_WaterbusServiceId_SeatTypeId",
                table: "service_seat_type_prices",
                columns: new[] { "WaterbusServiceId", "SeatTypeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE service_seat_type_down_map ON COMMIT DROP AS
                SELECT
                    price."WaterbusServiceId" AS service_id,
                    price."SeatTypeId" AS global_seat_type_id,
                    MD5(price."WaterbusServiceId"::text || price."SeatTypeId"::text || 'legacy')::uuid AS legacy_seat_type_id
                FROM service_seat_type_prices price;
                """);

            migrationBuilder.DropTable(
                name: "service_seat_type_prices");

            migrationBuilder.DropIndex(
                name: "IX_seat_types_Code",
                table: "seat_types");

            migrationBuilder.DropIndex(
                name: "IX_seat_types_DisplayOrder",
                table: "seat_types");

            migrationBuilder.AddColumn<Guid>(
                name: "WaterbusServiceId",
                table: "seat_types",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                INSERT INTO seat_types (
                    "Id",
                    "WaterbusServiceId",
                    "Code",
                    "Name",
                    "DisplayOrder",
                    "IsActive",
                    "Created",
                    "CreatedBy",
                    "LastModified",
                    "LastModifiedBy")
                SELECT
                    map.legacy_seat_type_id,
                    map.service_id,
                    seat_type."Code",
                    seat_type."Name",
                    seat_type."DisplayOrder",
                    seat_type."IsActive",
                    seat_type."Created",
                    seat_type."CreatedBy",
                    seat_type."LastModified",
                    seat_type."LastModifiedBy"
                FROM service_seat_type_down_map map
                JOIN seat_types seat_type ON seat_type."Id" = map.global_seat_type_id;

                UPDATE seats seat
                SET "SeatTypeId" = (
                    SELECT map.legacy_seat_type_id
                    FROM service_seat_type_down_map map
                    WHERE map.global_seat_type_id = seat."SeatTypeId"
                    ORDER BY CASE
                        WHEN map.service_id = vessel."WaterbusServiceId" THEN 0
                        ELSE 1
                    END
                    LIMIT 1
                )
                FROM vessels vessel
                WHERE seat."VesselId" = vessel."Id";

                DELETE FROM seat_types
                WHERE "WaterbusServiceId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "WaterbusServiceId",
                table: "seat_types",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "SeatSetupType",
                table: "vessels");

            migrationBuilder.CreateIndex(
                name: "IX_seat_types_WaterbusServiceId_Code",
                table: "seat_types",
                columns: new[] { "WaterbusServiceId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seat_types_WaterbusServiceId_DisplayOrder",
                table: "seat_types",
                columns: new[] { "WaterbusServiceId", "DisplayOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_seat_types_waterbus_services_WaterbusServiceId",
                table: "seat_types",
                column: "WaterbusServiceId",
                principalTable: "waterbus_services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
