using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260812110000_MariaDbBaseline")]
public sealed class MariaDbBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE `tests` (
                `Id` char(36) NOT NULL,
                `Title` varchar(300) NOT NULL,
                `Status` int NOT NULL,
                `ConcurrencyVersion` bigint NOT NULL DEFAULT 0,
                `passing_percentage` decimal(5,2) NOT NULL DEFAULT 70.00,
                `time_limit_minutes` int NULL,
                CONSTRAINT `PK_tests` PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE `published_test_revisions` (
                `Id` char(36) NOT NULL,
                `TestId` char(36) NOT NULL,
                `Version` int NOT NULL,
                `Title` varchar(300) NOT NULL,
                `PassingPercentage` decimal(5,2) NOT NULL,
                `TimeLimitMinutes` int NULL,
                `PublishedAt` datetime(6) NOT NULL,
                `questions_json` longtext NOT NULL,
                `ConcurrencyVersion` bigint NOT NULL DEFAULT 0,
                CONSTRAINT `PK_published_test_revisions` PRIMARY KEY (`Id`),
                UNIQUE KEY `IX_published_test_revisions_TestId_Version` (`TestId`, `Version`)
            ) CHARACTER SET utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE `test_assignments` (
                `Id` char(36) NOT NULL,
                `RevisionId` char(36) NOT NULL,
                `TargetType` int NOT NULL,
                `TargetId` varchar(256) NOT NULL,
                `AssignedBy` varchar(256) NOT NULL,
                `AssignedAt` datetime(6) NOT NULL,
                `AvailableFrom` datetime(6) NOT NULL,
                `AvailableUntil` datetime(6) NULL,
                `AttemptLimit` int NULL,
                `Status` int NOT NULL,
                `CancelledBy` varchar(256) NULL,
                `CancelledAt` datetime(6) NULL,
                `CancelReason` varchar(1000) NULL,
                `ConcurrencyVersion` bigint NOT NULL DEFAULT 0,
                CONSTRAINT `PK_test_assignments` PRIMARY KEY (`Id`),
                KEY `IX_test_assignments_TargetType_TargetId_Status` (`TargetType`, `TargetId`, `Status`),
                KEY `IX_test_assignments_RevisionId` (`RevisionId`)
            ) CHARACTER SET utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE `test_attempts` (
                `Id` char(36) NOT NULL,
                `AssignmentId` char(36) NOT NULL,
                `RevisionId` char(36) NOT NULL,
                `UserId` varchar(256) NOT NULL,
                `StartRequestId` char(36) NOT NULL,
                `Status` int NOT NULL,
                `StartedAt` datetime(6) NOT NULL,
                `DeadlineAt` datetime(6) NULL,
                `CompletedAt` datetime(6) NULL,
                `score_earned` decimal(18,2) NULL,
                `score_maximum` decimal(18,2) NULL,
                `Outcome` int NULL,
                `ConcurrencyVersion` bigint NOT NULL DEFAULT 0,
                CONSTRAINT `PK_test_attempts` PRIMARY KEY (`Id`),
                KEY `IX_test_attempts_AssignmentId_UserId` (`AssignmentId`, `UserId`),
                UNIQUE KEY `IX_test_attempts_AssignmentId_UserId_StartRequestId` (`AssignmentId`, `UserId`, `StartRequestId`),
                KEY `IX_test_attempts_RevisionId_Status_Outcome` (`RevisionId`, `Status`, `Outcome`)
            ) CHARACTER SET utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE `outbox_messages` (
                `Id` char(36) NOT NULL,
                `OccurredAt` datetime(6) NOT NULL,
                `Type` varchar(512) NOT NULL,
                `Payload` longtext NOT NULL,
                `ProcessedAt` datetime(6) NULL,
                `Error` longtext NULL,
                CONSTRAINT `PK_outbox_messages` PRIMARY KEY (`Id`),
                KEY `IX_outbox_messages_ProcessedAt_OccurredAt` (`ProcessedAt`, `OccurredAt`)
            ) CHARACTER SET utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE `idempotency_records` (
                `Id` char(36) NOT NULL,
                `Operation` varchar(200) NOT NULL,
                `ActorId` varchar(256) NOT NULL,
                `RequestId` char(36) NOT NULL,
                `ResultType` varchar(512) NOT NULL,
                `ResultJson` longtext NOT NULL,
                `CreatedAt` datetime(6) NOT NULL,
                CONSTRAINT `PK_idempotency_records` PRIMARY KEY (`Id`),
                UNIQUE KEY `IX_idempotency_records_Operation_ActorId_RequestId` (`Operation`, `ActorId`, `RequestId`)
            ) CHARACTER SET utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE `questions` (
                `Id` char(36) NOT NULL,
                `TestId` char(36) NOT NULL,
                `Text` varchar(2000) NOT NULL,
                `Type` int NOT NULL,
                `Points` decimal(18,2) NOT NULL,
                `Order` int NOT NULL,
                CONSTRAINT `PK_questions` PRIMARY KEY (`Id`),
                KEY `IX_questions_TestId` (`TestId`),
                CONSTRAINT `FK_questions_tests_TestId` FOREIGN KEY (`TestId`) REFERENCES `tests` (`Id`) ON DELETE CASCADE
            ) CHARACTER SET utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE `question_responses` (
                `TestAttemptId` char(36) NOT NULL,
                `Id` char(36) NOT NULL,
                `AnsweredAt` datetime(6) NULL,
                CONSTRAINT `PK_question_responses` PRIMARY KEY (`TestAttemptId`, `Id`),
                CONSTRAINT `FK_question_responses_test_attempts_TestAttemptId` FOREIGN KEY (`TestAttemptId`) REFERENCES `test_attempts` (`Id`) ON DELETE CASCADE
            ) CHARACTER SET utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE `answer_options` (
                `Id` char(36) NOT NULL,
                `QuestionId` char(36) NOT NULL,
                `Text` varchar(2000) NOT NULL,
                `IsCorrect` tinyint(1) NOT NULL,
                `Order` int NOT NULL,
                CONSTRAINT `PK_answer_options` PRIMARY KEY (`Id`),
                KEY `IX_answer_options_QuestionId` (`QuestionId`),
                CONSTRAINT `FK_answer_options_questions_QuestionId` FOREIGN KEY (`QuestionId`) REFERENCES `questions` (`Id`) ON DELETE CASCADE
            ) CHARACTER SET utf8mb4;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE `selected_answer_options` (
                `TestAttemptId` char(36) NOT NULL,
                `QuestionId` char(36) NOT NULL,
                `OptionId` char(36) NOT NULL,
                CONSTRAINT `PK_selected_answer_options` PRIMARY KEY (`TestAttemptId`, `QuestionId`, `OptionId`),
                CONSTRAINT `FK_selected_answer_options_question_responses_TestAttemptId_QuestionId`
                    FOREIGN KEY (`TestAttemptId`, `QuestionId`)
                    REFERENCES `question_responses` (`TestAttemptId`, `Id`) ON DELETE CASCADE
            ) CHARACTER SET utf8mb4;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS `selected_answer_options`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `answer_options`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `question_responses`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `questions`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `idempotency_records`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `outbox_messages`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `test_attempts`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `test_assignments`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `published_test_revisions`;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS `tests`;");
    }
}
