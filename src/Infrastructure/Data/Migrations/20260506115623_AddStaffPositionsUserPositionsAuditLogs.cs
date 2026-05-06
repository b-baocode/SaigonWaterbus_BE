using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffPositionsUserPositionsAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorUserId = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetTable = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: true),
                    OldValues = table.Column<string>(type: "jsonb", nullable: true),
                    NewValues = table.Column<string>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_logs_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "staff_positions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SystemName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
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
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PositionId = table.Column<int>(type: "integer", nullable: false),
                    StationId = table.Column<int>(type: "integer", nullable: true),
                    AssignedByUserId = table.Column<int>(type: "integer", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
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
                name: "IX_audit_logs_ActorUserId_CreatedAt",
                table: "audit_logs",
                columns: new[] { "ActorUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_TargetTable_TargetId_CreatedAt",
                table: "audit_logs",
                columns: new[] { "TargetTable", "TargetId", "CreatedAt" });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "audit_logs");
            migrationBuilder.DropTable(name: "user_positions");
            migrationBuilder.DropTable(name: "staff_positions");
        }
    }
}
