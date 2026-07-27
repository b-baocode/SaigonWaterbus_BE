using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLandmarkVoicesAndAudios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_landmarks_stations_station_id",
                table: "landmarks");

            migrationBuilder.DropIndex(
                name: "IX_landmarks_station_id",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "audio_en_url",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "audio_vi_url",
                table: "landmarks");

            migrationBuilder.RenameColumn(
                name: "station_id",
                table: "landmarks",
                newName: "route_id");

            migrationBuilder.AlterColumn<decimal>(
                name: "longitude",
                table: "landmarks",
                type: "numeric(10,7)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,7)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "latitude",
                table: "landmarks",
                type: "numeric(10,7)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,7)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "landmarks",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "display_order",
                table: "landmarks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "trigger_radius_m",
                table: "landmarks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "voices",
                columns: table => new
                {
                    voice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    region = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    style = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    vieneu_voice_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    sample_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voices", x => x.voice_id);
                });

            migrationBuilder.CreateTable(
                name: "landmark_audios",
                columns: table => new
                {
                    landmark_audio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    landmark_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audio_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    duration_seconds = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landmark_audios", x => x.landmark_audio_id);
                    table.ForeignKey(
                        name: "FK_landmark_audios_landmarks_landmark_id",
                        column: x => x.landmark_id,
                        principalTable: "landmarks",
                        principalColumn: "landmark_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_landmark_audios_voices_voice_id",
                        column: x => x.voice_id,
                        principalTable: "voices",
                        principalColumn: "voice_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_landmarks_route_id_display_order",
                table: "landmarks",
                columns: new[] { "route_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_landmark_audios_landmark_id_voice_id",
                table: "landmark_audios",
                columns: new[] { "landmark_id", "voice_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_landmark_audios_voice_id",
                table: "landmark_audios",
                column: "voice_id");

            migrationBuilder.AddForeignKey(
                name: "FK_landmarks_routes_route_id",
                table: "landmarks",
                column: "route_id",
                principalTable: "routes",
                principalColumn: "route_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_landmarks_routes_route_id",
                table: "landmarks");

            migrationBuilder.DropTable(
                name: "landmark_audios");

            migrationBuilder.DropTable(
                name: "voices");

            migrationBuilder.DropIndex(
                name: "IX_landmarks_route_id_display_order",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "display_order",
                table: "landmarks");

            migrationBuilder.DropColumn(
                name: "trigger_radius_m",
                table: "landmarks");

            migrationBuilder.RenameColumn(
                name: "route_id",
                table: "landmarks",
                newName: "station_id");

            migrationBuilder.AlterColumn<decimal>(
                name: "longitude",
                table: "landmarks",
                type: "numeric(10,7)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,7)");

            migrationBuilder.AlterColumn<decimal>(
                name: "latitude",
                table: "landmarks",
                type: "numeric(10,7)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,7)");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "landmarks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "audio_en_url",
                table: "landmarks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "audio_vi_url",
                table: "landmarks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_landmarks_station_id",
                table: "landmarks",
                column: "station_id");

            migrationBuilder.AddForeignKey(
                name: "FK_landmarks_stations_station_id",
                table: "landmarks",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "station_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
