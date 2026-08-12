using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TestApp.Infrastructure.Persistence.Migrations;

public partial class AddConcurrencyAndAttemptIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "ConcurrencyVersion",
            table: "tests",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "ConcurrencyVersion",
            table: "published_test_revisions",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "ConcurrencyVersion",
            table: "test_assignments",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "ConcurrencyVersion",
            table: "test_attempts",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<Guid>(
            name: "StartRequestId",
            table: "test_attempts",
            type: "TEXT",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.CreateIndex(
            name: "IX_test_attempts_AssignmentId_UserId_StartRequestId",
            table: "test_attempts",
            columns: new[] { "AssignmentId", "UserId", "StartRequestId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_test_attempts_AssignmentId_UserId_StartRequestId",
            table: "test_attempts");

        migrationBuilder.DropColumn(name: "ConcurrencyVersion", table: "tests");
        migrationBuilder.DropColumn(name: "ConcurrencyVersion", table: "published_test_revisions");
        migrationBuilder.DropColumn(name: "ConcurrencyVersion", table: "test_assignments");
        migrationBuilder.DropColumn(name: "ConcurrencyVersion", table: "test_attempts");
        migrationBuilder.DropColumn(name: "StartRequestId", table: "test_attempts");
    }
}
