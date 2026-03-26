using TestApp.Models.Enums;
using TestApp.Models.Identificators;

namespace TestApp.Models.Data.Teachers;

public sealed record TestItemDto(TestId Id, string TestName, TimeOnly TestDuration, DateTimeOffset CreatedAt, int AskCount);
public sealed record TestDetailTeacherDto(TestId Id, string TestName, TimeOnly TestDuration, DateTimeOffset CreatedAt, IReadOnlyCollection<AskTeacherDto> Asks);
public sealed record AskTeacherDto(AskId Id, string AskText, AskType AskType, IReadOnlyCollection<AnswerTeacherDto> Answers);
public sealed record AnswerTeacherDto(AnswerId Id, string AnswerText, AnswerCorrectness AnswerCorrect);
public sealed record CreateTestTeacherDto(string TestName, TimeOnly TestDuration);
public sealed record CreateAskTeacherDto(string AskText, AskType AskType);
public sealed record CreateAnswerTeacherDto(string AnswerText, AnswerCorrectness AnswerCorrect);
public sealed record UpdateTestTeacherDto(TestId Id, string TestName, TimeOnly TestDuration);
public sealed record UpdateAskTeacherDto(AskId Id, string AskText, AskType AskType);
public sealed record UpdateAnswerTeacherDto(AnswerId Id, string AnswerText, AnswerCorrectness AnswerCorrect);