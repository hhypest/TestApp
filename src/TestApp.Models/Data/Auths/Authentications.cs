using TestApp.Models.Enums;
using TestApp.Models.Identificators;

namespace TestApp.Models.Data.Auths;

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