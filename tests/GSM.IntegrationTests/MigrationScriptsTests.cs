using GSM.Infrastructure.Migrations;
using Xunit;

namespace GSM.IntegrationTests;

public class MigrationScriptsTests
{
    [Fact]
    public void Embedded_script_is_found_and_read()
    {
        var sql = MigrationScripts.Read("X0_Example");

        Assert.Contains("SELECT 1;", sql);
    }

    [Fact]
    public void Missing_script_fails_loudly()
    {
        Assert.Throws<InvalidOperationException>(() => MigrationScripts.Read("X0_DoesNotExist"));
    }
}
