using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonWaterbus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnsureUserCodeSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE SEQUENCE IF NOT EXISTS user_code_cu_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                CREATE SEQUENCE IF NOT EXISTS user_code_mg_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                CREATE SEQUENCE IF NOT EXISTS user_code_ad_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
                CREATE SEQUENCE IF NOT EXISTS user_code_st_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;

                SELECT setval('user_code_ad_seq',
                    GREATEST(COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'AD%'), 0), 1),
                    COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'AD%'), 0) > 0);

                SELECT setval('user_code_mg_seq',
                    GREATEST(COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'MG%'), 0), 1),
                    COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'MG%'), 0) > 0);

                SELECT setval('user_code_cu_seq',
                    GREATEST(COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'CU%'), 0), 1),
                    COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'CU%'), 0) > 0);

                SELECT setval('user_code_st_seq',
                    GREATEST(COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'ST%'), 0), 1),
                    COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'ST%'), 0) > 0);
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
