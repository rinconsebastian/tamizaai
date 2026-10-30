using Tamiza.Api.Mcp;

namespace Tamiza.Api.UnitTests.Mcp;

public sealed class UnsafeCodeTests
{
    private static string[] Found(string code) => UnsafeCode.Scan(code).Select(f => $"{f.Line}:{f.Found}").ToArray();

    [Theory]
    [InlineData("os")]
    [InlineData("sys")]
    [InlineData("subprocess")]
    [InlineData("socket")]
    [InlineData("shutil")]
    [InlineData("ctypes")]
    [InlineData("importlib")]
    [InlineData("http")]
    [InlineData("urllib")]
    public void Every_module_scripts_never_need_is_found_in_each_form_of_import(string module)
    {
        Assert.Equal([$"1:import of {module}"], Found($"import {module}"));
        Assert.Equal([$"1:import of {module}"], Found($"import {module}.sub"));
        Assert.Equal([$"1:import of {module}"], Found($"import {module} as m"));
        Assert.Equal([$"1:import of {module}"], Found($"from {module} import thing"));
        Assert.Equal([$"1:import of {module}"], Found($"from {module}.sub import thing"));
        Assert.Equal([$"2:import of {module}"], Found($"def f():\n    import {module}"));
        Assert.Equal([$"1:import of {module}"], Found($"import pandas as pd; import {module}"));
        Assert.Equal([$"1:import of {module}"], Found($"import pandas, {module}"));
    }

    [Theory]
    [InlineData("__import__('os')", "__import__")]
    [InlineData("eval('1')", "eval")]
    [InlineData("exec('x = 1')", "exec")]
    [InlineData("compile('1', 'f', 'eval')", "compile")]
    [InlineData("open('data.csv')", "open")]
    [InlineData("x = open ('data.csv')", "open")]
    [InlineData("(eval)('1')", "eval")]
    public void Dangerous_builtin_calls_are_found(string code, string call) => Assert.Equal([$"1:call of {call}"], Found(code));

    [Theory]
    [InlineData("df.open()")]
    [InlineData("re.compile('a')")]
    [InlineData("evaluate(x)")]
    [InlineData("reopen(x)")]
    [InlineData("execute(x)")]
    [InlineData("my_eval(x)")]
    [InlineData("opener = 1")]
    [InlineData("x = os_name")]
    [InlineData("import pandas as pd")]
    [InlineData("import numpy.linalg")]
    [InlineData("from pandas import DataFrame")]
    [InlineData("# import os")]
    [InlineData("osmosis = 1")]
    public void Names_that_only_look_like_them_are_not_found(string code) => Assert.Empty(Found(code));

    [Theory]
    [InlineData("http://example.org")]
    [InlineData("https://example.org/x")]
    [InlineData("ftp://example.org")]
    [InlineData("HTTPS://EXAMPLE.ORG")]
    public void Web_addresses_are_found_in_code_strings_and_comments(string address)
    {
        Assert.Equal(["1:web address"], Found($"url = '{address}'"));
        Assert.Equal(["1:web address"], Found($"# see {address}"));
        Assert.Equal(["2:web address"], Found($"x = 1\nrequests.get({address})"));
    }

    [Fact]
    public void The_lines_are_the_lines_of_the_code_and_each_finding_is_listed()
    {
        const string code = "import pandas as pd\nimport os\n\ndef clean(raw, ctx):\n    eval('1')  # https://x.example\n    return raw\n";

        Assert.Equal(["2:import of os", "5:call of eval", "5:web address"], Found(code));
    }

    [Fact]
    public void Nothing_is_found_in_the_starter_templates_or_in_nothing()
    {
        Assert.Empty(UnsafeCode.Scan(null));
        Assert.Empty(UnsafeCode.Scan(""));
        foreach (var kind in new[] { "clean", "output", "indicator" })
        {
            foreach (var template in Tamiza.Api.Assistant.AssistantPrompts.Examples(kind))
            {
                Assert.True(UnsafeCode.Scan(template.Source).Count == 0, $"{kind}/{template.Id}: {string.Join(", ", Found(template.Source))}");
            }
        }
    }

    [Fact]
    public void The_refusal_names_up_to_five_lines_and_keeps_all_of_them_in_its_details()
    {
        var code = string.Join("\n", Enumerable.Range(1, 7).Select(_ => "import os"));

        var refusal = UnsafeCode.RefusalFor(code)!;

        Assert.Equal("mcp.unsafeCode", refusal.Code);
        Assert.Contains("line 5: import of os", refusal.Message);
        Assert.DoesNotContain("line 6", refusal.Message);
        Assert.Contains("…", refusal.Message);
        Assert.Equal(7, ((System.Collections.IEnumerable)refusal.Details["lines"]!).Cast<object>().Count());
        Assert.Null(UnsafeCode.RefusalFor("def clean(raw, ctx):\n    return raw\n"));
    }

    [Fact]
    public void The_module_list_is_the_one_the_editor_warns_about()
    {
        var folder = FindUiFolder();
        if (folder is null)
        {
            return; // The UI sources are not next to the API (a build of the API alone).
        }

        var editor = File.ReadAllText(Path.Combine(folder, "python-modules.json"));
        var copy = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Tamiza.Api", "Mcp", "python-modules.json"));

        Assert.Equal(editor.ReplaceLineEndings("\n").Trim(), copy.ReplaceLineEndings("\n").Trim());
        Assert.Contains("socket", UnsafeCode.NeverModules);
    }

    /// <summary>The editor's folder, found by walking up from the test assembly, as the other drift tests do.</summary>
    private static string? FindUiFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tamiza-ui", "src", "app", "pages", "projects", "script-editor");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
