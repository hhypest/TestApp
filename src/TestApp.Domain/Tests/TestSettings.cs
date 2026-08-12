using TestApp.Core.Monads;
using TestApp.Domain.Common;

namespace TestApp.Domain.Tests;

public sealed class TestSettings
{
    public decimal PassingPercentage { get; private set; }
    public int? TimeLimitMinutes { get; private set; }

    private TestSettings() { PassingPercentage = 70m; }

    private TestSettings(decimal passingPercentage, int? timeLimitMinutes)
    {
        PassingPercentage = passingPercentage;
        TimeLimitMinutes = timeLimitMinutes;
    }

    public static TestSettings Default() => new(70m, null);

    public static Result<TestSettings, DomainError> Create(decimal passingPercentage, int? timeLimitMinutes)
    {
        if (passingPercentage is < 0m or > 100m)
            return DomainError.Validation("test.settings.passing_percentage", "Passing percentage must be between 0 and 100.");
        if (timeLimitMinutes is <= 0)
            return DomainError.Validation("test.settings.time_limit", "Time limit must be greater than zero when specified.");

        return new TestSettings(passingPercentage, timeLimitMinutes);
    }
}
