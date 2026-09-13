using GSM.Client;

namespace GSM.Web.Tests;

public class GsmRolesTests
{
    [Theory]
    [InlineData(GsmRoles.Owner, "/")]
    [InlineData(GsmRoles.Staff, "/")]
    [InlineData(GsmRoles.SuperAdmin, "/admin")]
    [InlineData(GsmRoles.Member, "/app")]
    [InlineData(null, "/login")]
    [InlineData("unknown", "/login")]
    public void HomeFor_routes_each_role_to_its_area(string? role, string expected)
        => Assert.Equal(expected, GsmRoles.HomeFor(role));
}
