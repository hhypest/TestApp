using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812151000_OutboxDeadLetterManagement")]
public sealed class OutboxDeadLetterManagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DiscardedAt",
            table: "outbox_messages",
            type: "datetime(6)",
            nullable: true);

        migrationBuilder.Sql("DROP INDEX `IX_outbox_delivery_queue` ON `outbox_messages`;");
        migrationBuilder.Sql("""
            CREATE INDEX `IX_outbox_delivery_queue`
            ON `outbox_messages` (`ProcessedAt`, `DeadLetteredAt`, `DiscardedAt`, `NextAttemptAt`, `OccurredAt`);
            """);

        migrationBuilder.CreateTable(
            name: "outbox_dead_letter_actions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                EventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                Action = table.Column<int>(type: "int", nullable: false),
                ActorId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                CorrelationId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_outbox_dead_letter_actions", x => x.Id))
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "IX_outbox_dead_letter_actions_EventId_OccurredAt",
            table: "outbox_dead_letter_actions",
            columns: new[] { "EventId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_dead_letter_actions_OccurredAt",
            table: "outbox_dead_letter_actions",
            column: "OccurredAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_dead_letter_actions");

        migrationBuilder.Sql("DROP INDEX `IX_outbox_delivery_queue` ON `outbox_messages`;");
        migrationBuilder.Sql("""
            CREATE INDEX `IX_outbox_delivery_queue`
            ON `outbox_messages` (`ProcessedAt`, `DeadLetteredAt`, `NextAttemptAt`, `OccurredAt`);
            """);

        migrationBuilder.DropColumn(
            name: "DiscardedAt",
            table: "outbox_messages");
    }
}
