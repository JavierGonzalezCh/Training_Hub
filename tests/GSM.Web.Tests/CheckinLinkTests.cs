using GSM.Member.UI;
using GSM.UI.Shared.Auth;
using Xunit;

namespace GSM.Web.Tests;

public class ReturnUrlTests
{
    [Theory]
    [InlineData("/app/checkin?t=r123.abc", "/app/checkin?t=r123.abc")]
    [InlineData("/members/42", "/members/42")]
    [InlineData("  /app  ", "/app")]
    public void Local_paths_are_accepted(string input, string expected)
        => Assert.Equal(expected, ReturnUrl.Sanitize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://otro-sitio")]
    [InlineData("//otro-sitio")]
    [InlineData("/\\otro-sitio")]
    [InlineData("javascript:alert(1)")]
    [InlineData("app/checkin")]
    [InlineData("/ruta\r\nSet-Cookie: x=1")]
    public void Anything_that_is_not_a_local_path_is_ignored(string? input)
        => Assert.Null(ReturnUrl.Sanitize(input));

    [Fact]
    public void Login_url_keeps_the_target_escaped()
    {
        // El área del miembro entra por su propia pantalla (/app/entrar); el panel, por /login.
        Assert.Equal("/app/entrar?returnUrl=%2Fapp%2Fcheckin%3Ft%3Dr1.abc", ReturnUrl.LoginFor("app/checkin?t=r1.abc"));
        Assert.Equal("/app/entrar?returnUrl=%2Fapp", ReturnUrl.LoginFor("app"));
        Assert.Equal("/login?returnUrl=%2Fsettings", ReturnUrl.LoginFor("settings"));
        Assert.Equal("/login?returnUrl=%2Fapplications", ReturnUrl.LoginFor("applications"));
        Assert.Equal("/login", ReturnUrl.LoginFor(""));
    }
}

public class GymCodeLinkTests
{
    [Theory]
    [InlineData("https://gym.test/app/checkin?t=r123.abc-_", "r123.abc-_")]
    [InlineData("https://gym.test/app/checkin?x=1&t=s1.zzz", "s1.zzz")]
    [InlineData("s1.zzz", "s1.zzz")]
    public void Extracts_the_code_from_a_link_or_a_bare_code(string scanned, string expected)
        => Assert.Equal(expected, GymCodeLink.ExtractCode(scanned));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://gym.test/app/checkin")]
    [InlineData("texto con espacios")]
    public void Unusable_scans_give_no_code(string? scanned)
        => Assert.Null(GymCodeLink.ExtractCode(scanned));

    [Fact]
    public void Path_escapes_the_code()
        => Assert.Equal("/app/checkin?t=r1.a%2Bb", GymCodeLink.PathFor("r1.a+b"));
}
