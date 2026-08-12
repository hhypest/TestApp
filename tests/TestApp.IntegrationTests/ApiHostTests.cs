using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class ApiHostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiHostTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Keycloak:Authority", "https://identity.invalid/realms/testapp");
            builder.UseSetting("Keycloak:Audience", "testapp-api");
        });
    }

    [Fact]
    public async Task Protected_endpoint_rejects_anonymous_request()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/me/assignments", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
