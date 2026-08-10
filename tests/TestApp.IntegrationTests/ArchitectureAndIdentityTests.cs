using System.Security.Claims;
using TestApp.Domain.Tests;
using TestApp.Infrastructure.Identity;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class ArchitectureAndIdentityTests
{
    [Fact]
    public void Domain_does_not_reference_outer_layers()
    {
        var references = typeof(Test).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("TestApp.Application", references);
        Assert.DoesNotContain("TestApp.Infrastructure", references);
        Assert.DoesNotContain("TestApp.Api", references);
    }

    [Fact]
    public void Application_does_not_reference_infrastructure_or_api()
    {
        var references = typeof(TestApp.Application.Abstractions.IUnitOfWork).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("TestApp.Infrastructure", references);
        Assert.DoesNotContain("TestApp.Api", references);
    }

    [Fact]
    public void Keycloak_claims_mapper_maps_subject_groups_and_roles()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", "user-42"),
            new Claim("groups", "[\"students\",\"reviewers\"]"),
            new Claim(ClaimTypes.Role, "test-author"),
            new Claim("roles", "[\"offline_access\",\"uma_authorization\"]")
        ], "test");

        var result = KeycloakClaimsMapper.Map(new ClaimsPrincipal(identity));

        Assert.Equal("user-42", result.UserId.Value);
        Assert.Contains(result.Groups, x => x.Value == "students");
        Assert.Contains(result.Groups, x => x.Value == "reviewers");
        Assert.Contains("test-author", result.Roles);
        Assert.Contains("offline_access", result.Roles);
    }

    [Fact]
    public void Keycloak_claims_mapper_requires_subject()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "test"));

        Assert.Throws<UnauthorizedAccessException>(() => KeycloakClaimsMapper.Map(principal));
    }
}
