using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketScanAuditConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ticket_scan_events_performed_by_user_id_action_client_opera~",
                table: "ticket_scan_events",
                columns: new[] { "performed_by_user_id", "action", "client_operation_id" },
                unique: true,
                filter: "\"client_operation_id\" IS NOT NULL AND \"result\" = 'Success' AND \"action\" IN ('CheckIn', 'CheckOut')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ticket_scan_events_performed_by_user_id_action_client_opera~",
                table: "ticket_scan_events");
        }
    }
}
