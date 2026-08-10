using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TestApp.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260810170500_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "tests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_tests", x => x.Id));

        migrationBuilder.CreateTable(
            name: "published_test_revisions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TestId = table.Column<Guid>(type: "TEXT", nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                questions_json = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_published_test_revisions", x => x.Id));

        migrationBuilder.CreateTable(
            name: "test_assignments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                target = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                AssignedBy = table.Column<string>(type: "TEXT", nullable: false),
                AssignedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                AvailableFrom = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                AvailableUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                AttemptLimit = table.Column<int>(type: "INTEGER", nullable: true),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                CancelledBy = table.Column<string>(type: "TEXT", nullable: true),
                CancelledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                CancelReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_test_assignments", x => x.Id));

        migrationBuilder.CreateTable(
            name: "test_attempts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AssignmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                UserId = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                score_earned = table.Column<decimal>(type: "TEXT", nullable: true),
                score_maximum = table.Column<decimal>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_test_attempts", x => x.Id));

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Payload = table.Column<string>(type: "TEXT", nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                Error = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_outbox_messages", x => x.Id));

        migrationBuilder.CreateTable(
            name: "questions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TestId = table.Column<Guid>(type: "TEXT", nullable: false),
                Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                Type = table.Column<int>(type: "INTEGER", nullable: false),
                Points = table.Column<decimal>(type: "TEXT", nullable: false),
                Order = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_questions", x => x.Id);
                table.ForeignKey("FK_questions_tests_TestId", x => x.TestId, "tests", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "question_responses",
            columns: table => new
            {
                TestAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AnsweredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_question_responses", x => new { x.TestAttemptId, x.Id });
                table.ForeignKey("FK_question_responses_test_attempts_TestAttemptId", x => x.TestAttemptId, "test_attempts", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "answer_options",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                IsCorrect = table.Column<bool>(type: "INTEGER", nullable: false),
                Order = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_answer_options", x => x.Id);
                table.ForeignKey("FK_answer_options_questions_QuestionId", x => x.QuestionId, "questions", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "selected_answer_options",
            columns: table => new
            {
                TestAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                OptionId = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_selected_answer_options", x => new { x.TestAttemptId, x.QuestionId, x.OptionId });
                table.ForeignKey(
                    "FK_selected_answer_options_question_responses_TestAttemptId_QuestionId",
                    x => new { x.TestAttemptId, x.QuestionId },
                    "question_responses",
                    new[] { "TestAttemptId", "Id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_questions_TestId", "questions", "TestId");
        migrationBuilder.CreateIndex("IX_answer_options_QuestionId", "answer_options", "QuestionId");
        migrationBuilder.CreateIndex("IX_test_attempts_AssignmentId_UserId", "test_attempts", new[] { "AssignmentId", "UserId" });
        migrationBuilder.CreateIndex("IX_outbox_messages_ProcessedAt_OccurredAt", "outbox_messages", new[] { "ProcessedAt", "OccurredAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("answer_options");
        migrationBuilder.DropTable("outbox_messages");
        migrationBuilder.DropTable("published_test_revisions");
        migrationBuilder.DropTable("selected_answer_options");
        migrationBuilder.DropTable("test_assignments");
        migrationBuilder.DropTable("questions");
        migrationBuilder.DropTable("question_responses");
        migrationBuilder.DropTable("tests");
        migrationBuilder.DropTable("test_attempts");
    }
}
