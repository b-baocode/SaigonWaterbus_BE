using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserCode",
                table: "users",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_UserCode",
                table: "users",
                column: "UserCode",
                unique: true,
                filter: "\"UserCode\" IS NOT NULL");

            migrationBuilder.Sql("""
                CREATE SEQUENCE user_code_cu_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                CREATE SEQUENCE user_code_mg_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                CREATE SEQUENCE user_code_ad_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                CREATE SEQUENCE user_code_st_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                CREATE SEQUENCE user_code_op_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                """);

            migrationBuilder.Sql("""
                WITH primary_role AS (
                    SELECT
                        ura."UserId",
                        CASE r."Code"
                            WHEN 'CU01' THEN 'CU'
                            WHEN 'MG01' THEN 'MG'
                            WHEN 'AD01' THEN 'AD'
                            WHEN 'ST01' THEN 'ST'
                            WHEN 'OP01' THEN 'OP'
                        END AS prefix,
                        ROW_NUMBER() OVER (
                            PARTITION BY ura."UserId"
                            ORDER BY ura."AssignedAt" DESC, ura."Id" DESC
                        ) AS role_rank
                    FROM user_role_assignments AS ura
                    INNER JOIN roles AS r ON r."Id" = ura."RoleId"
                    WHERE ura."IsActive" = TRUE
                ),
                ranked AS (
                    SELECT
                        "UserId",
                        prefix,
                        ROW_NUMBER() OVER (
                            PARTITION BY prefix
                            ORDER BY "UserId"
                        ) AS sequence_number
                    FROM primary_role
                    WHERE role_rank = 1
                      AND prefix IS NOT NULL
                )
                UPDATE users AS u
                SET "UserCode" = ranked.prefix || LPAD(ranked.sequence_number::text, 7, '0')
                FROM ranked
                WHERE u."Id" = ranked."UserId";
                """);

            migrationBuilder.Sql("""
                SELECT setval('user_code_cu_seq',
                    GREATEST(COALESCE((SELECT MAX(SUBSTRING("UserCode" FROM 3)::integer) FROM users WHERE "UserCode" LIKE 'CU%'), 0), 1),
                    COALESCE((SELECT MAX(SUBSTRING("UserCode" FROM 3)::integer) FROM users WHERE "UserCode" LIKE 'CU%'), 0) > 0);

                SELECT setval('user_code_mg_seq',
                    GREATEST(COALESCE((SELECT MAX(SUBSTRING("UserCode" FROM 3)::integer) FROM users WHERE "UserCode" LIKE 'MG%'), 0), 1),
                    COALESCE((SELECT MAX(SUBSTRING("UserCode" FROM 3)::integer) FROM users WHERE "UserCode" LIKE 'MG%'), 0) > 0);

                SELECT setval('user_code_ad_seq',
                    GREATEST(COALESCE((SELECT MAX(SUBSTRING("UserCode" FROM 3)::integer) FROM users WHERE "UserCode" LIKE 'AD%'), 0), 1),
                    COALESCE((SELECT MAX(SUBSTRING("UserCode" FROM 3)::integer) FROM users WHERE "UserCode" LIKE 'AD%'), 0) > 0);

                SELECT setval('user_code_st_seq',
                    GREATEST(COALESCE((SELECT MAX(SUBSTRING("UserCode" FROM 3)::integer) FROM users WHERE "UserCode" LIKE 'ST%'), 0), 1),
                    COALESCE((SELECT MAX(SUBSTRING("UserCode" FROM 3)::integer) FROM users WHERE "UserCode" LIKE 'ST%'), 0) > 0);

                SELECT setval('user_code_op_seq',
                    GREATEST(COALESCE((SELECT MAX(SUBSTRING("UserCode" FROM 3)::integer) FROM users WHERE "UserCode" LIKE 'OP%'), 0), 1),
                    COALESCE((SELECT MAX(SUBSTRING("UserCode" FROM 3)::integer) FROM users WHERE "UserCode" LIKE 'OP%'), 0) > 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_UserCode",
                table: "users");

            migrationBuilder.DropColumn(
                name: "UserCode",
                table: "users");

            migrationBuilder.Sql("""
                DROP SEQUENCE IF EXISTS user_code_cu_seq;
                DROP SEQUENCE IF EXISTS user_code_mg_seq;
                DROP SEQUENCE IF EXISTS user_code_ad_seq;
                DROP SEQUENCE IF EXISTS user_code_st_seq;
                DROP SEQUENCE IF EXISTS user_code_op_seq;
                """);
        }
    }
}
