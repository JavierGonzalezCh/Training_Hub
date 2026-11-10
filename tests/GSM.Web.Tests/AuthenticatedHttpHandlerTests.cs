using System.Net;
using GSM.Client.Http;
using Xunit;

namespace GSM.Web.Tests;

/// <summary>E5 — cuando la renovación falla: se respeta lo que otra pestaña ya guardó o se avisa a la interfaz.</summary>
public sealed class AuthenticatedHttpHandlerTests
{
    private sealed class Refresher(Func<AuthTokens?> result) : ITokenRefresher
    {
        public Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
            => Task.FromResult(result());
    }

    private sealed class Inner(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private static AuthTokens Tokens(string access, string refresh)
        => new(access, refresh, DateTimeOffset.UtcNow.AddMinutes(10));

    [Fact]
    public async Task A_failed_refresh_clears_the_session_and_tells_the_ui()
    {
        var store = new InMemoryTokenStore();
        await store.SetAsync(Tokens("old", "r1"));
        var events = new SessionEvents();
        var ended = 0;
        events.Ended += () => ended++;
        var inner = new Inner(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var handler = new AuthenticatedHttpHandler(store, new Refresher(() => null), events) { InnerHandler = inner };

        var response = await new HttpClient(handler) { BaseAddress = new Uri("http://x/") }.GetAsync("api/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await store.GetAsync());
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task If_another_tab_already_saved_new_tokens_they_are_used_instead_of_clearing()
    {
        var store = new InMemoryTokenStore();
        await store.SetAsync(Tokens("old", "r1"));
        var events = new SessionEvents();
        var ended = 0;
        events.Ended += () => ended++;
        // La renovación falla porque la otra pestaña ya rotó el refresh token y dejó el par nuevo guardado.
        var refresher = new Refresher(() =>
        {
            store.SetAsync(Tokens("fresh", "r2")).AsTask().Wait();
            return null;
        });
        var inner = new Inner(r => r.Headers.Authorization?.Parameter == "fresh"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var handler = new AuthenticatedHttpHandler(store, refresher, events) { InnerHandler = inner };

        var response = await new HttpClient(handler) { BaseAddress = new Uri("http://x/") }.GetAsync("api/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("r2", (await store.GetAsync())!.RefreshToken);
        Assert.Equal(0, ended);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_refresh_that_could_not_be_done_keeps_the_session(HttpStatusCode refreshStatus)
    {
        var store = new InMemoryTokenStore();
        await store.SetAsync(Tokens("old", "r1"));
        var events = new SessionEvents();
        var ended = 0;
        events.Ended += () => ended++;
        var api = new Inner(r => r.RequestUri!.AbsolutePath.EndsWith("refresh", StringComparison.Ordinal)
            ? new HttpResponseMessage(refreshStatus)
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var refresher = new HttpTokenRefresher(new HttpClient(api) { BaseAddress = new Uri("http://x/") });
        var handler = new AuthenticatedHttpHandler(store, refresher, events) { InnerHandler = api };

        var response = await new HttpClient(handler) { BaseAddress = new Uri("http://x/") }.GetAsync("api/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("r1", (await store.GetAsync())!.RefreshToken);
        Assert.Equal(0, ended);
    }

    [Fact]
    public async Task A_refresh_rejected_by_the_server_ends_the_session()
    {
        var store = new InMemoryTokenStore();
        await store.SetAsync(Tokens("old", "r1"));
        var events = new SessionEvents();
        var ended = 0;
        events.Ended += () => ended++;
        var api = new Inner(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var refresher = new HttpTokenRefresher(new HttpClient(api) { BaseAddress = new Uri("http://x/") });
        var handler = new AuthenticatedHttpHandler(store, refresher, events) { InnerHandler = api };

        await new HttpClient(handler) { BaseAddress = new Uri("http://x/") }.GetAsync("api/ping");

        Assert.Null(await store.GetAsync());
        Assert.Equal(1, ended);
    }

    [Fact]
    public void While_a_screen_holds_the_session_its_end_waits_until_released()
    {
        var events = new SessionEvents();
        var ended = 0;
        var whileHeld = 0;
        events.Ended += () => ended++;
        events.EndedWhileHeld += () => whileHeld++;

        var hold = events.Hold();
        events.GetType().GetMethod("RaiseEnded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(events, null);

        Assert.Equal(0, ended);
        Assert.Equal(1, whileHeld);
        Assert.True(events.EndPending);

        hold.Dispose();
        Assert.Equal(1, ended);
        Assert.False(events.EndPending);
    }
}
