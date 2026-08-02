using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    public partial class FixRefreshTokenRlsPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE public.refresh_tokens ENABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS ""Allow app management of refresh_tokens"" ON public.refresh_tokens;

                CREATE POLICY ""Allow app management of refresh_tokens""
                ON public.refresh_tokens
                FOR ALL
                TO public
                USING (true)
                WITH CHECK (true);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS ""Allow app management of refresh_tokens"" ON public.refresh_tokens;
            ");
        }
    }
}