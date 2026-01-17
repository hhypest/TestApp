namespace TestApp.Core.Data;

#region Teacher DTOs
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
#endregion

#region Student DTOs
public sealed record TestDetailStudentDto(TestId Id, string TestName, TimeOnly TestDuration, DateTimeOffset CreatedAt, IReadOnlyCollection<AskStudentDto> Asks);
public sealed record AskStudentDto(AskId Id, string AskText, AskType AskType, IReadOnlyCollection<AnswerStudentDto> Answers);
public sealed record AnswerStudentDto(AnswerId Id, string AnswerText);
#endregion

#region Test Submits\Results DTOs
public sealed record SubmitTestDto(TestId TestId, IReadOnlyCollection<SubmitAnswerDto> Answers);
public sealed record SubmitAnswerDto(AskId AskId, IReadOnlyCollection<AnswerId> SelectedAnswers);
public sealed record TestResultDetailDto(TestId TestId, int TotalAsks, int CorrectAnswers, double ScorePercentage, DateTimeOffset SubmittedAt, PassType IsPassed, IReadOnlyCollection<AskResultDto> Asks);
public sealed record AskResultDto(AskId AskId, string AskText, AskType AskType, IReadOnlyCollection<AnswerResultDto> Answers);
public sealed record AnswerResultDto(AnswerId AnswerId, string AnswerText, bool IsSelected, bool IsCorrect);
#endregion

#region Authentication\Authorization DTOs
public sealed record UserDto(UserId Id, string Login, string Email, DateTimeOffset CreatedAt, IReadOnlyCollection<RoleDto> Roles);
public sealed record CreateUserDto(string Login, string Password, string Email);
public sealed record LoginUserDto(string Login, string Password);
public sealed record UpdateUserDto(string Login, string Email);
public sealed record ChangePasswordDto(string CurrentPassword, string NewPassword);
public sealed record RefreshTokenDto(string RefreshToken);
public sealed record TokenPairDto(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);
public sealed record RoleDto(RoleType RoleName, IReadOnlyCollection<RoleClaimDto> RoleClaims);
public sealed record RoleClaimDto(string ClaimType, string ClaimValue);
public sealed record CreateRoleDto(RoleType RoleName);
public sealed record CreateRoleClaimDto(RoleId RoleId, string ClaimType, string ClaimValue);
public sealed record UpdateRoleClaimDto(RoleClaimId Id, string ClaimType, string ClaimValue);
public sealed record AssignRoleToUserDto(UserId UserId, RoleType RoleName);
public sealed record RemoveRoleFromUserDto(UserId UserId, RoleType RoleName);
#endregion