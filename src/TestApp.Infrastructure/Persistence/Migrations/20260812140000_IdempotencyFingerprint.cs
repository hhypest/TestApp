using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812140000_IdempotencyFingerprint")]
public sealed class IdempotencyFingerprint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE `idempotency_records`
                ADD COLUMN `RequestFingerprint` varchar(64) NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE `idempotency_records` DROP COLUMN `RequestFingerprint`;");
    }
}
