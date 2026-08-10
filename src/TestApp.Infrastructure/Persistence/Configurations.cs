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
        b.OwnsMany(x => x.Questions, q =>
        {
            q.ToTable("questions");
            q.WithOwner().HasForeignKey("TestId");
            q.HasKey(x => x.Id);
            q.Property(x => x.Id).HasConversion(x => x.Value, x => new QuestionId(x));
            q.Property(x => x.Text).HasMaxLength(2000).IsRequired();
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
        b.Property<string>("QuestionsJson").HasColumnName("questions_json").IsRequired();
        b.Ignore(x => x.Questions);
        b.Ignore(x => x.DomainEvents);
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
        b.Property<string>("TargetType").HasMaxLength(16).IsRequired();
        b.Property<string>("TargetId").HasMaxLength(200).IsRequired();
        b.Ignore(x => x.Target);
        b.Ignore(x => x.DomainEvents);
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
        b.Property(x => x.UserId).HasConversion(x => x.Value, x => new ExternalUserId(x));
        b.Property<string>("AnswersJson").HasColumnName("answers_json").IsRequired().HasDefaultValue("{}");
        b.Ignore(x => x.Answers);
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => new { x.AssignmentId, x.UserId });
    }
}
