using System.Text.Json;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.Domain.Tests.Unit;

/// <summary>
/// The value-object contract of the model (STAB-009, ADR-029): strong identifiers and
/// <see cref="AttemptScore"/> validate themselves rather than trusting their callers.
/// </summary>
public sealed class ValueObjectInvariantTests
{
    public static TheoryData<string, Func<Guid, object>> IdentifierConstructors => new()
    {
        { nameof(TestId), value => new TestId(value) },
        { nameof(QuestionId), value => new QuestionId(value) },
        { nameof(AnswerOptionId), value => new AnswerOptionId(value) },
        { nameof(TestAssignmentId), value => new TestAssignmentId(value) },
        { nameof(TestAttemptId), value => new TestAttemptId(value) },
        { nameof(PublishedTestRevisionId), value => new PublishedTestRevisionId(value) }
    };

    [Theory]
    [MemberData(nameof(IdentifierConstructors))]
    public void A_strong_identifier_refuses_the_empty_guid(string identifier, Func<Guid, object> construct)
    {
        var error = Assert.Throws<ArgumentException>(() => construct(Guid.Empty));

        Assert.Contains("empty GUID", error.Message, StringComparison.Ordinal);
        Assert.NotNull(identifier);
    }

    [Theory]
    [MemberData(nameof(IdentifierConstructors))]
    public void A_strong_identifier_accepts_a_real_guid(string identifier, Func<Guid, object> construct)
    {
        var value = Guid.CreateVersion7();

        var id = construct(value);

        Assert.NotNull(id);
        Assert.NotNull(identifier);
    }

    [Fact]
    public void Generated_identifiers_are_never_empty()
    {
        Assert.NotEqual(Guid.Empty, TestId.New().Value);
        Assert.NotEqual(Guid.Empty, QuestionId.New().Value);
        Assert.NotEqual(Guid.Empty, AnswerOptionId.New().Value);
        Assert.NotEqual(Guid.Empty, TestAssignmentId.New().Value);
        Assert.NotEqual(Guid.Empty, TestAttemptId.New().Value);
        Assert.NotEqual(Guid.Empty, PublishedTestRevisionId.New().Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void External_identifiers_refuse_blank_values(string value)
    {
        Assert.Throws<ArgumentException>(() => new ExternalUserId(value));
        Assert.Throws<ArgumentException>(() => new ExternalGroupId(value));
        Assert.Throws<ArgumentException>(() => ExternalUserId.FromSubject(value));
        Assert.Throws<ArgumentException>(() => ExternalGroupId.FromExternalId(value));
    }

    [Fact]
    public void External_identifiers_refuse_values_over_the_length_limit()
    {
        var tooLong = new string('u', ExternalIdentityLimits.MaxIdentifierLength + 1);

        Assert.Throws<ArgumentException>(() => new ExternalUserId(tooLong));
        Assert.Throws<ArgumentException>(() => new ExternalGroupId(tooLong));
    }

    /// <summary>
    /// The constructor and the named factory have to enforce the same rule — before STAB-009 the
    /// constructor enforced nothing, so the factory could be bypassed by calling <c>new</c>.
    /// </summary>
    [Fact]
    public void The_external_identifier_constructor_and_factory_agree()
    {
        var atLimit = new string('u', ExternalIdentityLimits.MaxIdentifierLength);

        Assert.Equal(ExternalUserId.FromSubject(atLimit), new ExternalUserId(atLimit));
        Assert.Equal(ExternalGroupId.FromExternalId(atLimit), new ExternalGroupId(atLimit));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(1, -10)]
    [InlineData(11, 10)]
    [InlineData(0.01, 0)]
    public void A_score_refuses_values_outside_zero_to_maximum(decimal earned, decimal maximum) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttemptScore(earned, maximum));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 10)]
    [InlineData(10, 10)]
    [InlineData(2.5, 10)]
    public void A_score_accepts_values_inside_zero_to_maximum(decimal earned, decimal maximum)
    {
        var score = new AttemptScore(earned, maximum);

        Assert.Equal(earned, score.Earned);
        Assert.Equal(maximum, score.Maximum);
    }

    /// <summary>
    /// The published revision stores its questions as a single `jsonb` column, so the strong identifiers
    /// inside <see cref="PublishedQuestion"/> are round-tripped by <c>System.Text.Json</c> on every read.
    /// That path needs an accessible parameterised constructor, which is why ADR-029 validates in the
    /// constructor instead of hiding it behind a static factory. This test pins the shape of the stored
    /// payload and the fact that it still materialises.
    /// </summary>
    [Fact]
    public void The_published_question_payload_round_trips_through_system_text_json()
    {
        var questionId = QuestionId.New();
        var optionId = AnswerOptionId.New();
        var questions = new List<PublishedQuestion>
        {
            new(questionId, "Pick one", QuestionType.SingleChoice, 2m, 0,
                [new PublishedAnswerOption(optionId, "Correct", 0, true)])
        };

        var json = JsonSerializer.Serialize(questions, JsonSerializerOptions.Default);
        var restored = JsonSerializer.Deserialize<List<PublishedQuestion>>(json, JsonSerializerOptions.Default);

        Assert.Contains($"\"Value\":\"{questionId.Value}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"IsCorrect\":true", json, StringComparison.Ordinal);
        Assert.NotNull(restored);
        var question = Assert.Single(restored);
        Assert.Equal(questionId, question.Id);
        Assert.Equal(optionId, Assert.Single(question.Options).Id);
    }
}
