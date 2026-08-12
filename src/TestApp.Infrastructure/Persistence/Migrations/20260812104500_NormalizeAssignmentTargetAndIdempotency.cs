using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812104500_NormalizeAssignmentTargetAndIdempotency")]
public sealed class NormalizeAssignmentTargetAndIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "TargetType",
            table: "test_assignments",
            type: "INTEGER",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "TargetId",
            table: "test_assignments",
            type: "TEXT",
            maxLength: 256,
            nullable: false,
            defaultValue: string.Empty);

        migrationBuilder.Sql("UPDATE test_assignments SET TargetType = CASE WHEN target LIKE 'group:%' THEN 2 ELSE 1 END, TargetId = CASE WHEN instr(target, ':') > 0 THEN substr(target, instr(target, ':') + 1) ELSE target END");

        migrationBuilder.CreateIndex(
            name: "IX_test_assignments_TargetType_TargetId_Status",
            table: "test_assignments",
            columns: new[] { "TargetType", "TargetId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_test_assignments_RevisionId",
            table: "test_assignments",
            column: "RevisionId");

        migrationBuilder.CreateIndex(
            name: "IX_test_attempts_RevisionId_Status_Outcome",
            table: "test_attempts",
            columns: new[] { "RevisionId", "Status", "Outcome" });

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Operation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                ResultType = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_idempotency_records", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_Operation_ActorId_RequestId",
            table: "idempotency_records",
            columns: new[] { "Operation", "ActorId", "RequestId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("idempotency_records");
        migrationBuilder.DropIndex(name: "IX_test_assignments_TargetType_TargetId_Status", table: "test_assignments");
        migrationBuilder.DropIndex(name: "IX_test_assignments_RevisionId", table: "test_assignments");
        migrationBuilder.DropIndex(name: "IX_test_attempts_RevisionId_Status_Outcome", table: "test_attempts");
        migrationBuilder.Sql("UPDATE test_assignments SET target = CASE WHEN TargetType = 2 THEN 'group:' || TargetId ELSE 'user:' || TargetId END");
    }
}
