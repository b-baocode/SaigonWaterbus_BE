using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVesselImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vessel_images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VesselId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PublicId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vessel_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vessel_images_vessels_VesselId",
                        column: x => x.VesselId,
                        principalTable: "vessels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO vessel_images ("Id", "VesselId", "Url", "PublicId", "DisplayOrder", "IsPrimary", "Created", "CreatedBy", "LastModified", "LastModifiedBy")
                SELECT gen_random_uuid(), v."Id", v."ImageUrl", v."ImagePublicId", 1, TRUE, now(), NULL, now(), NULL
                FROM vessels v
                WHERE v."ImageUrl" IS NOT NULL
                  AND length(trim(v."ImageUrl")) > 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_vessel_images_VesselId_DisplayOrder",
                table: "vessel_images",
                columns: new[] { "VesselId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_vessel_images_VesselId_IsPrimary",
                table: "vessel_images",
                columns: new[] { "VesselId", "IsPrimary" },
                unique: true,
                filter: "\"IsPrimary\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vessel_images");
        }
    }
}
