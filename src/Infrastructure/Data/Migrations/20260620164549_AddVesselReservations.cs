using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVesselReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS btree_gist;""");

            migrationBuilder.CreateTable(
                name: "vessel_reservations",
                columns: table => new
                {
                    vessel_reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vessel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    release_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vessel_reservations", x => x.vessel_reservation_id);
                    table.ForeignKey(
                        name: "FK_vessel_reservations_users_confirmed_by_user_id",
                        column: x => x.confirmed_by_user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_vessel_reservations_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_vessel_reservations_vessels_vessel_id",
                        column: x => x.vessel_id,
                        principalTable: "vessels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vessel_reservations_confirmed_by_user_id",
                table: "vessel_reservations",
                column: "confirmed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_vessel_reservations_created_by_user_id",
                table: "vessel_reservations",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_vessel_reservations_source_type_source_id",
                table: "vessel_reservations",
                columns: new[] { "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vessel_reservations_status_expires_at",
                table: "vessel_reservations",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_vessel_reservations_vessel_id_start_at_end_at",
                table: "vessel_reservations",
                columns: new[] { "vessel_id", "start_at", "end_at" });

            migrationBuilder.Sql(
                """
                WITH candidates AS (
                    SELECT
                        c.custom_booking_request_id AS source_id,
                        c.assigned_vessel_id AS vessel_id,
                        ((c.departure_date::timestamp + c.preferred_start_time) AT TIME ZONE 'Asia/Ho_Chi_Minh') AS start_at,
                        CASE
                            WHEN c.estimated_end_date IS NOT NULL AND c.preferred_end_time IS NOT NULL
                                THEN ((c.estimated_end_date::timestamp + c.preferred_end_time) AT TIME ZONE 'Asia/Ho_Chi_Minh')
                            ELSE ((c.departure_date::timestamp + c.preferred_start_time) AT TIME ZONE 'Asia/Ho_Chi_Minh')
                                + make_interval(mins => GREATEST(1, c.estimated_duration_minutes))
                        END AS end_at,
                        CASE
                            WHEN c.status = 'Confirmed' AND q.deposit_payment_status = 'Paid' THEN 'Confirmed'
                            ELSE 'Held'
                        END AS status,
                        CASE
                            WHEN c.status = 'Confirmed' AND q.deposit_payment_status = 'Paid' THEN NULL
                            WHEN q.valid_until IS NOT NULL THEN q.valid_until
                            ELSE now() + interval '2 hours'
                        END AS expires_at,
                        c.assigned_by_user_id AS created_by_user_id,
                        CASE WHEN c.status = 'Confirmed' AND q.deposit_payment_status = 'Paid' THEN c.user_id ELSE NULL END AS confirmed_by_user_id,
                        CASE WHEN c.status = 'Confirmed' AND q.deposit_payment_status = 'Paid' THEN COALESCE(c.quote_accepted_at, q.deposit_payment_paid_at) ELSE NULL END AS confirmed_at,
                        CASE WHEN c.status = 'Confirmed' AND q.deposit_payment_status = 'Paid' THEN 0 ELSE 1 END AS priority,
                        COALESCE(c.quote_accepted_at, q.deposit_payment_paid_at, c.quoted_at, c.assigned_at, c.created_at, now()) AS sort_at
                    FROM custom_booking_requests c
                    LEFT JOIN custom_booking_quotes q ON q.custom_booking_request_id = c.custom_booking_request_id
                    WHERE c.assigned_vessel_id IS NOT NULL
                      AND c.preferred_start_time IS NOT NULL
                      AND c.status <> 'Cancelled'
                ),
                non_overlapping_candidates AS (
                    SELECT c.*
                    FROM candidates c
                    WHERE c.end_at > c.start_at
                      AND NOT EXISTS (
                          SELECT 1
                          FROM candidates other
                          WHERE other.vessel_id = c.vessel_id
                            AND other.source_id <> c.source_id
                            AND other.start_at < c.end_at
                            AND other.end_at > c.start_at
                            AND (
                                other.priority < c.priority
                                OR (
                                    other.priority = c.priority
                                    AND (other.sort_at, other.source_id) < (c.sort_at, c.source_id)
                                )
                            )
                      )
                )
                INSERT INTO vessel_reservations (
                    vessel_reservation_id,
                    vessel_id,
                    source_type,
                    source_id,
                    start_at,
                    end_at,
                    status,
                    expires_at,
                    created_by_user_id,
                    confirmed_by_user_id,
                    confirmed_at,
                    released_at,
                    release_reason,
                    created_at,
                    updated_at)
                SELECT
                    gen_random_uuid(),
                    vessel_id,
                    'CustomBooking',
                    source_id,
                    start_at,
                    end_at,
                    status,
                    expires_at,
                    created_by_user_id,
                    confirmed_by_user_id,
                    confirmed_at,
                    NULL,
                    NULL,
                    now(),
                    now()
                FROM non_overlapping_candidates;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE vessel_reservations
                ADD CONSTRAINT "CK_vessel_reservations_valid_window"
                CHECK (end_at > start_at);
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE vessel_reservations
                ADD CONSTRAINT "EX_vessel_reservations_no_overlap_active"
                EXCLUDE USING gist (
                    vessel_id WITH =,
                    tstzrange(start_at, end_at, '[)') WITH &&
                )
                WHERE (status IN ('Held', 'PaymentPending', 'Confirmed'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE vessel_reservations
                DROP CONSTRAINT IF EXISTS "EX_vessel_reservations_no_overlap_active";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE vessel_reservations
                DROP CONSTRAINT IF EXISTS "CK_vessel_reservations_valid_window";
                """);

            migrationBuilder.DropTable(
                name: "vessel_reservations");
        }
    }
}
