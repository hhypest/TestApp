using Microsoft.EntityFrameworkCore;
using TestApp.Application.Queries;
using TestApp.Domain.Identity;
using TestApp.Domain.Tests;

namespace TestApp.Infrastructure.Persistence;

public sealed partial class ReadModelQueries
{
    public async Task<TestEditorView?> GetTestEditorViewAsync(
        TestId testId,
        ExternalUserId? ownerId,
        CancellationToken ct)
    {
        var query = _db.Tests.AsNoTracking()
            .Include(test => test.Questions)
            .ThenInclude(question => question.Options)
            .Where(test => test.Id == testId);
        if (ownerId is { } owner)
            query = query.Where(test => test.OwnerId == owner);

        var test = await query.SingleOrDefaultAsync(ct);
        if (test is null)
            return null;

        return new TestEditorView(
            test.Id,
            test.Title,
            test.Status,
            test.ConcurrencyVersion,
            test.Settings.PassingPercentage,
            test.Settings.TimeLimitMinutes,
            test.Questions
                .OrderBy(question => question.Order)
                .Select(question => new QuestionEditorView(
                    question.Id,
                    question.Text,
                    question.Type,
                    question.Points,
                    question.Order,
                    question.Options
                        .OrderBy(option => option.Order)
                        .Select(option => new AnswerOptionEditorView(
                            option.Id,
                            option.Text,
                            option.IsCorrect,
                            option.Order))
                        .ToArray()))
                .ToArray());
    }
}
