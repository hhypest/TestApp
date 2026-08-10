using TestApp.Domain.Events;

namespace TestApp.Domain.Tests;

public sealed record TestPublished : DomainEvent
{
    public TestPublished(TestId testId, DateTimeOffset occurredAt) : base(occurredAt)
    {
        TestId = testId;
    }

    public TestId TestId { get; }
}
