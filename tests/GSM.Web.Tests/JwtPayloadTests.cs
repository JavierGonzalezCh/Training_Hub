using System.Text;
using System.Text.Json;
using GSM.Client.Http;

namespace GSM.Web.Tests;

public class JwtPayloadTests
{
    [Fact]
    public void Reads_role_gym_and_impersonating_claims()
    {
        var gymId = Guid.NewGuid();
        var exp = DateTimeOffset.UtcNow.AddMinutes(30);
        var token = FakeJwt(new
        {
            sub = "user-1",
            role = "owner",
            name = "Ana Owner",
            gym_id = gymId.ToString(),
            impersonating = "true",
            exp = exp.ToUnixTimeSeconds(),
        });

        var payload = JwtPayload.TryRead(token);

        Assert.NotNull(payload);
        Assert.Equal("owner", payload!.Role);
        Assert.Equal("Ana Owner", payload.Name);
        Assert.Equal(gymId, payload.GymId);
        Assert.True(payload.Impersonating);
        Assert.False(payload.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Treats_a_token_past_its_exp_as_expired()
    {
        var token = FakeJwt(new { sub = "user-1", role = "member", exp = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds() });

        var payload = JwtPayload.TryRead(token);

        Assert.NotNull(payload);
        Assert.True(payload!.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Returns_null_for_garbage_input()
    {
        Assert.Null(JwtPayload.TryRead(null));
        Assert.Null(JwtPayload.TryRead(""));
        Assert.Null(JwtPayload.TryRead("not-a-jwt"));
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
