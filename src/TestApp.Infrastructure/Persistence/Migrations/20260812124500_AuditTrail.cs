using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812124500_AuditTrail")]
public sealed class AuditTrail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE `audit_entries` (
                `Id` char(36) NOT NULL,
                `OccurredAt` datetime(6) NOT NULL,
                `ActorId` varchar(256) NULL,
                `Method` varchar(16) NOT NULL,
                `Route` varchar(500) NOT NULL,
                `StatusCode` int NOT NULL,
                `CorrelationId` varchar(128) NOT NULL,
                `TraceId` varchar(64) NOT NULL,
                `DurationMs` decimal(18,2) NOT NULL,
                CONSTRAINT `PK_audit_entries` PRIMARY KEY (`Id`),
                KEY `IX_audit_entries_OccurredAt` (`OccurredAt`),
                KEY `IX_audit_entries_ActorId_OccurredAt` (`ActorId`, `OccurredAt`),
                KEY `IX_audit_entries_StatusCode_OccurredAt` (`StatusCode`, `OccurredAt`)
            ) CHARACTER SET utf8mb4;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE `audit_entries`;");
    }
}
