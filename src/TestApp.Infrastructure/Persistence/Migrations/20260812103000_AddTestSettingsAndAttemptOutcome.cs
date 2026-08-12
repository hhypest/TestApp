using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812103000_AddTestSettingsAndAttemptOutcome")]
public sealed class AddTestSettingsAndAttemptOutcome : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "passing_percentage",
            table: "tests",
            type: "TEXT",
            nullable: false,
            defaultValue: 70m);

        migrationBuilder.AddColumn<int>(
            name: "time_limit_minutes",
            table: "tests",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PassingPercentage",
            table: "published_test_revisions",
            type: "TEXT",
            nullable: false,
            defaultValue: 70m);

        migrationBuilder.AddColumn<int>(
            name: "TimeLimitMinutes",
            table: "published_test_revisions",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DeadlineAt",
            table: "test_attempts",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Outcome",
            table: "test_attempts",
            type: "INTEGER",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("passing_percentage", "tests");
        migrationBuilder.DropColumn("time_limit_minutes", "tests");
        migrationBuilder.DropColumn("PassingPercentage", "published_test_revisions");
        migrationBuilder.DropColumn("TimeLimitMinutes", "published_test_revisions");
        migrationBuilder.DropColumn("DeadlineAt", "test_attempts");
        migrationBuilder.DropColumn("Outcome", "test_attempts");
    }
}
