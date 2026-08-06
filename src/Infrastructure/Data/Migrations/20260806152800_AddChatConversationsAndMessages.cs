using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatConversationsAndMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_conversations",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anonymous_session_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_assistant_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    inactivity_deadline_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    retention_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    auto_close_message_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_conversations", x => x.conversation_id);
                    table.ForeignKey(
                        name: "FK_chat_conversations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_auto_close_message = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_messages", x => x.message_id);
                    table.ForeignKey(
                        name: "FK_chat_messages_chat_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "chat_conversations",
                        principalColumn: "conversation_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversations_anonymous_session_id",
                table: "chat_conversations",
                column: "anonymous_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversations_retention_expires_at",
                table: "chat_conversations",
                column: "retention_expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversations_status_inactivity_deadline_at",
                table: "chat_conversations",
                columns: new[] { "status", "inactivity_deadline_at" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversations_user_id",
                table: "chat_conversations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_messages_conversation_id_sequence_number",
                table: "chat_messages",
                columns: new[] { "conversation_id", "sequence_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_messages");

            migrationBuilder.DropTable(
                name: "chat_conversations");
        }
    }
}
