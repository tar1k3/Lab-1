namespace SecureLab.Api.Tests;

// ЛР 6: автоматизована частина REG-XSS-01 — клієнт не повертає небезпечний HTML sink.
// Візуальна частина (marker у браузері) лишається ручною процедурою ЛР 5.
public sealed class XssRegressionTests
{
    private static string ClientFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SecureLab.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "SecureLab.Api", "Client", name));
    }

    [Fact]
    public void ClientScript_DoesNotUseDangerousInnerHtmlSink()
    {
        var script = ClientFile("browser-lab.js");

        // Опис і коментарі пишуться лише через safe helper setText (textContent)
        Assert.DoesNotContain("innerHTML", script);
        Assert.DoesNotContain("outerHTML", script);
        Assert.DoesNotContain("insertAdjacentHTML", script);
        Assert.Contains("element.textContent = value", script);
        Assert.Contains("setText(document.querySelector(\"#description\"), current.description)", script);
        Assert.Contains("setText(paragraph, comment.text)", script);
    }
}
