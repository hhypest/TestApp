using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Infrastructure.Persistence;

public sealed class TestConfiguration : IEntityTypeConfiguration<Test>
{
    public void Configure(EntityTypeBuilder<Test> b)
    {
        b.ToTable("tests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new TestId(x));
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.OwnerId).HasConversion(x => x.Value, x => new ExternalUserId(x)).HasMaxLength(256).IsRequired();
        b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.OwnsOne(x => x.Settings, s =>
        {
            s.Property(x => x.PassingPercentage).HasColumnName("passing_percentage").HasPrecision(5, 2);
            s.Property(x => x.TimeLimitMinutes).HasColumnName("time_limit_minutes");
        });
        b.OwnsMany(x => x.Questions, q =>
        {
            q.ToTable("questions");
            q.WithOwner().HasForeignKey("TestId");
            q.HasKey(x => x.Id);
            q.Property(x => x.Id).HasConversion(x => x.Value, x => new QuestionId(x));
            q.Property(x => x.Text).HasMaxLength(2000).IsRequired();
            q.Property(x => x.Points).HasPrecision(18, 2);
            q.OwnsMany(x => x.Options, o =>
            {
                o.ToTable("answer_options");
                o.WithOwner().HasForeignKey("QuestionId");
                o.HasKey(x => x.Id);
                o.Property(x => x.Id).HasConversion(x => x.Value, x => new AnswerOptionId(x));
                o.Property(x => x.Text).HasMaxLength(2000).IsRequired();
            });
        });
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.OwnerId, x.Status });
    }
}

public sealed class RevisionConfiguration : IEntityTypeConfiguration<PublishedTestRevision>
{
    public void Configure(EntityTypeBuilder<PublishedTestRevision> b)
    {
        b.ToTable("published_test_revisions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new PublishedTestRevisionId(x));
        b.Property(x => x.TestId).HasConversion(x => x.Value, x => new TestId(x));
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.PassingPercentage).HasPrecision(5, 2);
        b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.Ignore(x => x.Questions);
        b.Property<List<PublishedQuestion>>("_questions")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<List<PublishedQuestion>>(v, JsonSerializerOptions.Default) ?? new List<PublishedQuestion>())
            .HasColumnName("questions_json")
            .HasColumnType("longtext");
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.TestId, x.Version }).IsUnique();
    }
}

public sealed class AssignmentConfiguration : IEntityTypeConfiguration<TestAssignment>
{
    public void Configure(EntityTypeBuilder<TestAssignment> b)
    {
        b.ToTable("test_assignments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new TestAssignmentId(x));
        b.Property(x => x.RevisionId).HasConversion(x => x.Value, x => new PublishedTestRevisionId(x));
        b.Property(x => x.AssignedBy).HasConversion(x => x.Value, x => new ExternalUserId(x)).HasMaxLength(256).IsRequired();
        b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.Property(x => x.TargetType).IsRequired();
        b.Property(x => x.TargetId).HasMaxLength(256).IsRequired();
        b.Property(x => x.CancelledBy)
            .HasConversion(x => x.HasValue ? x.Value.Value : null, x => x == null ? (ExternalUserId?)null : new ExternalUserId(x))
            .HasMaxLength(256);
        b.Property(x => x.CancelReason).HasMaxLength(1000);
        b.Ignore(x => x.Target);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.TargetType, x.TargetId, x.Status });
        b.HasIndex(x => x.RevisionId);
    }
}

public sealed class AttemptConfiguration : IEntityTypeConfiguration<TestAttempt>
{
    public void Configure(EntityTypeBuilder<TestAttempt> b)
    {
        b.ToTable("test_attempts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion(x => x.Value, x => new TestAttemptId(x));
        b.Property(x => x.AssignmentId).HasConversion(x => x.Value, x => new TestAssignmentId(x));
        b.Property(x => x.RevisionId).HasConversion(x => x.Value, x => new PublishedTestRevisionId(x));
        b.Property(x => x.UserId).HasConversion(x => x.Value, x => new ExternalUserId(x)).HasMaxLength(256).IsRequired();
        b.Property(x => x.StartRequestId).IsRequired();
        b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.OwnsOne(x => x.Score, score =>
        {
            score.Property(x => x.Earned).HasColumnName("score_earned").HasPrecision(18, 2);
            score.Property(x => x.Maximum).HasColumnName("score_maximum").HasPrecision(18, 2);
            score.Ignore(x => x.Percentage);
        });
        b.OwnsMany(x => x.Responses, response =>
        {
            response.ToTable("question_responses");
            response.WithOwner().HasForeignKey("TestAttemptId");
            response.Property(x => x.Id).HasConversion(x => x.Value, x => new QuestionId(x));
            response.HasKey("TestAttemptId", nameof(QuestionResponse.Id));
            response.OwnsMany(x => x.SelectedOptions, selected =>
            {
                selected.ToTable("selected_answer_options");
                selected.WithOwner().HasForeignKey("TestAttemptId", "QuestionId");
                selected.Property<TestAttemptId>("TestAttemptId").HasConversion(x => x.Value, x => new TestAttemptId(x));
                selected.Property<QuestionId>("QuestionId").HasConversion(x => x.Value, x => new QuestionId(x));
                selected.Property(x => x.OptionId).HasConversion(x => x.Value, x => new AnswerOptionId(x));
                selected.HasKey("TestAttemptId", "QuestionId", nameof(SelectedAnswerOption.OptionId));
            });
        });
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.AssignmentId, x.UserId });
        b.HasIndex(x => new { x.AssignmentId, x.UserId, x.StartRequestId }).IsUnique();
        b.HasIndex(x => new { x.RevisionId, x.Status, x.Outcome });
    }
}
