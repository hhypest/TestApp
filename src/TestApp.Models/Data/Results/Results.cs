using TestApp.Models.Enums;
using TestApp.Models.Identificators;

namespace TestApp.Models.Data.Results;

public sealed record SubmitTestDto(TestId TestId, IReadOnlyCollection<SubmitAnswerDto> Answers);
public sealed record SubmitAnswerDto(AskId AskId, IReadOnlyCollection<AnswerId> SelectedAnswers);
public sealed record TestResultDetailDto(TestId TestId, int TotalAsks, int CorrectAnswers, double ScorePercentage, DateTimeOffset SubmittedAt, PassType IsPassed, IReadOnlyCollection<AskResultDto> Asks);
public sealed record AskResultDto(AskId AskId, string AskText, AskType AskType, IReadOnlyCollection<AnswerResultDto> Answers);
public sealed record AnswerResultDto(AnswerId AnswerId, string AnswerText, bool IsSelected, bool IsCorrect);