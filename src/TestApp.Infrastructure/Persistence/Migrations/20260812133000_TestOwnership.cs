using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812133000_TestOwnership")]
public sealed class TestOwnership : Migration
{
    public const string LegacyOwnerId = "__legacy_admin_only__";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            ALTER TABLE `tests`
                ADD COLUMN `OwnerId` varchar(256) NOT NULL DEFAULT '{LegacyOwnerId}';
            """);

        migrationBuilder.Sql("""
            ALTER TABLE `tests`
                ALTER COLUMN `OwnerId` DROP DEFAULT;
            """);

        migrationBuilder.Sql("""
            CREATE INDEX `IX_tests_OwnerId_Status`
            ON `tests` (`OwnerId`, `Status`);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX `IX_tests_OwnerId_Status` ON `tests`;");
        migrationBuilder.Sql("ALTER TABLE `tests` DROP COLUMN `OwnerId`;");
    }
}
