using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GSM.Client.Http;
using GSM.UI.Shared.Auth;

namespace GSM.Web.Tests;

public class GsmAuthenticationStateProviderTests
{
    [Fact]
    public async Task No_tokens_means_anonymous()
    {
        var provider = new GsmAuthenticationStateProvider(new InMemoryTokenStore());

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task Valid_token_yields_role_and_gym_claims()
    {
        var store = new InMemoryTokenStore();
        var gymId = Guid.NewGuid();
        await store.SetAsync(new AuthTokens(
            FakeJwt(new { sub = "u1", role = "owner", name = "Ana", gym_id = gymId.ToString(), exp = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds() }),
            "refresh",
            DateTimeOffset.UtcNow.AddMinutes(30)));

        var provider = new GsmAuthenticationStateProvider(store);
        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity?.IsAuthenticated);
        Assert.Equal("owner", state.User.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal(gymId, state.User.GymId());
        Assert.False(state.User.IsImpersonating());
    }

    [Fact]
    public async Task Expired_token_is_treated_as_anonymous()
    {
        var store = new InMemoryTokenStore();
        await store.SetAsync(new AuthTokens(
            FakeJwt(new { sub = "u1", role = "owner", exp = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds() }),
            "refresh",
            DateTimeOffset.UtcNow.AddMinutes(-1)));

        var provider = new GsmAuthenticationStateProvider(store);
        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    private static string FakeJwt(object payload)
    {
        var header = Base64Url("""{"alg":"none","typ":"JWT"}"""u8.ToArray());
        var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{body}.signature";
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
