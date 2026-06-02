using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStaffPositionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_positions");

            migrationBuilder.DropTable(
                name: "staff_positions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_positions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    SystemName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_positions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssignedByUserId = table.Column<int>(type: "integer", nullable: true),
                    PositionId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StationId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_positions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_positions_staff_positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "staff_positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_positions_users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_positions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_staff_positions_Code",
                table: "staff_positions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_positions_SystemName",
                table: "staff_positions",
                column: "SystemName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_positions_AssignedByUserId",
                table: "user_positions",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_positions_PositionId",
                table: "user_positions",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_user_positions_StationId",
                table: "user_positions",
                column: "StationId",
                filter: "\"StationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_positions_UserId_PositionId",
                table: "user_positions",
                columns: new[] { "UserId", "PositionId" },
                unique: true,
                filter: "\"IsActive\" = true AND \"StationId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_positions_UserId_PositionId_StationId",
                table: "user_positions",
                columns: new[] { "UserId", "PositionId", "StationId" },
                unique: true,
                filter: "\"IsActive\" = true AND \"StationId\" IS NOT NULL");
        }
    }
}
