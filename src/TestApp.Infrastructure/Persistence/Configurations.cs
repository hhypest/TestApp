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
        b.ToTable("tests"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasConversion(x => x.Value, x => new TestId(x)); b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.OwnsMany(x => x.Questions, q => { q.ToTable("questions"); q.WithOwner().HasForeignKey("TestId"); q.HasKey(x => x.Id); q.Property(x => x.Id).HasConversion(x => x.Value, x => new QuestionId(x)); q.Property(x => x.Text).HasMaxLength(2000).IsRequired(); q.OwnsMany(x => x.Options, o => { o.ToTable("answer_options"); o.WithOwner().HasForeignKey("QuestionId"); o.HasKey(x => x.Id); o.Property(x => x.Id).HasConversion(x => x.Value, x => new AnswerOptionId(x)); o.Property(x => x.Text).HasMaxLength(2000).IsRequired(); }); });
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class RevisionConfiguration : IEntityTypeConfiguration<PublishedTestRevision>
{
    public void Configure(EntityTypeBuilder<PublishedTestRevision> b)
    {
        b.ToTable("published_test_revisions"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasConversion(x => x.Value, x => new PublishedTestRevisionId(x)); b.Property(x => x.TestId).HasConversion(x => x.Value, x => new TestId(x)); b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Questions).HasConversion(v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default), v => JsonSerializer.Deserialize<PublishedQuestion[]>(v, JsonSerializerOptions.Default) ?? Array.Empty<PublishedQuestion>()).HasColumnName("questions_json");
        b.Ignore(x => x.DomainEvents);
    }
}

public sealed class AssignmentConfiguration : IEntityTypeConfiguration<TestAssignment>
{
    public void Configure(EntityTypeBuilder<TestAssignment> b)
    {
        b.ToTable("test_assignments"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasConversion(x => x.Value, x => new TestAssignmentId(x)); b.Property(x => x.RevisionId).HasConversion(x => x.Value, x => new PublishedTestRevisionId(x));
        b.Property(x => x.Target).HasConversion(v => SerializeTarget(v), v => DeserializeTarget(v)).HasColumnName("target").HasMaxLength(512);
        b.Ignore(x => x.DomainEvents);
    }
    private static string SerializeTarget(AssignmentTarget target) => target switch { AssignmentTarget.User u => $"user:{u.UserId.Value}", AssignmentTarget.Group g => $"group:{g.GroupId.Value}", _ => throw new InvalidOperationException() };
    private static AssignmentTarget DeserializeTarget(string value) => value.StartsWith("user:", StringComparison.Ordinal) ? new AssignmentTarget.User(ExternalUserId.FromSubject(value[5..])) : value.StartsWith("group:", StringComparison.Ordinal) ? new AssignmentTarget.Group(ExternalGroupId.FromExternalId(value[6..])) : throw new InvalidOperationException("Unknown assignment target.");
}

public sealed class AttemptConfiguration : IEntityTypeConfiguration<TestAttempt>
{
    public void Configure(EntityTypeBuilder<TestAttempt> b)
    {
        b.ToTable("test_attempts"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasConversion(x => x.Value, x => new TestAttemptId(x)); b.Property(x => x.AssignmentId).HasConversion(x => x.Value, x => new TestAssignmentId(x)); b.Property(x => x.UserId).HasConversion(x => x.Value, x => new ExternalUserId(x));
        b.Property(x => x.Answers).HasConversion(v => JsonSerializer.Serialize(v.ToDictionary(x => x.Key.Value, x => x.Value.Select(o => o.Value).ToArray()), JsonSerializerOptions.Default), v => (IReadOnlyDictionary<QuestionId, IReadOnlyCollection<AnswerOptionId>>)(JsonSerializer.Deserialize<Dictionary<Guid, Guid[]>>(v, JsonSerializerOptions.Default) ?? []).ToDictionary(x => new QuestionId(x.Key), x => (IReadOnlyCollection<AnswerOptionId>)x.Value.Select(id => new AnswerOptionId(id)).ToArray())).HasColumnName("answers_json");
        b.Ignore(x => x.DomainEvents); b.HasIndex(x => new { x.AssignmentId, x.UserId });
    }
}
