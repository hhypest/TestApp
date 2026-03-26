using TestApp.Models.Enums;
using TestApp.Models.Identificators;

namespace TestApp.Models.Data.Students;

public sealed record TestDetailStudentDto(TestId Id, string TestName, TimeOnly TestDuration, DateTimeOffset CreatedAt, IReadOnlyCollection<AskStudentDto> Asks);
public sealed record AskStudentDto(AskId Id, string AskText, AskType AskType, IReadOnlyCollection<AnswerStudentDto> Answers);
public sealed record AnswerStudentDto(AnswerId Id, string AnswerText);