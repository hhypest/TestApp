using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812115000_OutboxDeliveryState")]
public sealed class OutboxDeliveryState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE `outbox_messages`
                ADD COLUMN `AttemptCount` int NOT NULL DEFAULT 0,
                ADD COLUMN `LastAttemptAt` datetime(6) NULL,
                ADD COLUMN `NextAttemptAt` datetime(6) NULL,
                ADD COLUMN `DeadLetteredAt` datetime(6) NULL;
            """);

        migrationBuilder.Sql("""
            DROP INDEX `IX_outbox_messages_ProcessedAt_OccurredAt` ON `outbox_messages`;
            """);

        migrationBuilder.Sql("""
            CREATE INDEX `IX_outbox_delivery_queue`
            ON `outbox_messages` (`ProcessedAt`, `DeadLetteredAt`, `NextAttemptAt`, `OccurredAt`);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX `IX_outbox_delivery_queue` ON `outbox_messages`;");
        migrationBuilder.Sql("""
            ALTER TABLE `outbox_messages`
                DROP COLUMN `AttemptCount`,
                DROP COLUMN `LastAttemptAt`,
                DROP COLUMN `NextAttemptAt`,
                DROP COLUMN `DeadLetteredAt`;
            """);
        migrationBuilder.Sql("""
            CREATE INDEX `IX_outbox_messages_ProcessedAt_OccurredAt`
            ON `outbox_messages` (`ProcessedAt`, `OccurredAt`);
            """);
    }
}
