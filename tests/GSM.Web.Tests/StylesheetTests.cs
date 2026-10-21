using System.Text.RegularExpressions;

namespace GSM.Web.Tests;

public class StylesheetTests
{
    [Fact]
    public void GsmCss_has_balanced_braces()
    {
        var dir = AppContext.BaseDirectory;
        string? path = null;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "src", "Frontend", "GSM.UI.Shared", "wwwroot", "gsm.css");
            if (File.Exists(candidate)) { path = candidate; break; }
        }
        Assert.NotNull(path);

        var css = Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/", "", RegexOptions.Singleline);
        Assert.Equal(css.Count(c => c == '{'), css.Count(c => c == '}'));
    }
}
