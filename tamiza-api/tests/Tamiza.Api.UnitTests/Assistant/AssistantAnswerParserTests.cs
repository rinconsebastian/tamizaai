using Tamiza.Api.Assistant;

namespace Tamiza.Api.UnitTests.Assistant;

/// <summary>Splitting an answer into its explanation and its code blocks (design D8).</summary>
public sealed class AssistantAnswerParserTests
{
    [Fact]
    public void One_python_block_becomes_a_block_and_a_note_in_place()
    {
        var split = AssistantAnswerParser.Split("Here is the script.\n\n```python\nimport pandas as pd\n\ndef compute(cleaned, ctx):\n    return cleaned[\"main\"]\n```\n\nIt reads main.");

        var block = Assert.Single(split.Blocks);
        Assert.Equal("import pandas as pd\n\ndef compute(cleaned, ctx):\n    return cleaned[\"main\"]", block.Code);
        Assert.True(block.Complete);
        Assert.Equal("Here is the script.\n\n[proposed code: 4 lines]\n\nIt reads main.", split.Explanation);
    }

    [Theory]
    [InlineData("```py")]
    [InlineData("```Python")]
    [InlineData("```")]
    [InlineData("```python title=\"x\"")]
    public void Python_py_and_bare_fences_are_blocks(string fence)
    {
        var split = AssistantAnswerParser.Split($"Text\n{fence}\nx = 1\n```");

        Assert.Equal("x = 1", Assert.Single(split.Blocks).Code);
    }

    [Fact]
    public void Several_blocks_keep_their_order()
    {
        var split = AssistantAnswerParser.Split("One:\n```python\na = 1\n```\nTwo:\n```python\nb = 2\nc = 3\n```");

        Assert.Equal(["a = 1", "b = 2\nc = 3"], split.Blocks.Select(b => b.Code).ToArray());
        Assert.Equal("One:\n[proposed code: 1 lines]\nTwo:\n[proposed code: 2 lines]", split.Explanation);
    }

    [Fact]
    public void A_block_that_is_not_closed_is_incomplete_and_still_noted()
    {
        var split = AssistantAnswerParser.Split("Cut off:\n```python\nimport pandas as pd\n\ndef compute");

        var block = Assert.Single(split.Blocks);
        Assert.False(block.Complete);
        Assert.Equal("import pandas as pd\n\ndef compute", block.Code);
        Assert.Equal("Cut off:\n[proposed code: 3 lines]", split.Explanation);
    }

    [Fact]
    public void A_fence_of_another_language_stays_in_the_explanation()
    {
        var text = "Install nothing.\n```sh\npip install scikit-learn\n```\nThen:\n```python\nx = 1\n```";

        var split = AssistantAnswerParser.Split(text);

        Assert.Equal("x = 1", Assert.Single(split.Blocks).Code);
        Assert.Equal("Install nothing.\n```sh\npip install scikit-learn\n```\nThen:\n[proposed code: 1 lines]", split.Explanation);
    }

    [Fact]
    public void A_longer_fence_closes_only_with_at_least_as_many_backticks()
    {
        var split = AssistantAnswerParser.Split("````python\ntext = \"\"\"\n```\n\"\"\"\n````");

        Assert.Equal("text = \"\"\"\n```\n\"\"\"", Assert.Single(split.Blocks).Code);
    }

    [Fact]
    public void An_answer_with_no_code_is_only_the_explanation()
    {
        var split = AssistantAnswerParser.Split("Which column holds the municipality?");

        Assert.Empty(split.Blocks);
        Assert.Equal("Which column holds the municipality?", split.Explanation);
    }

    [Fact]
    public void An_empty_block_is_dropped_and_windows_line_ends_are_normalised()
    {
        var split = AssistantAnswerParser.Split("Nothing:\r\n```python\r\n\r\n```\r\nDone\r\n```python\r\nx = 1\r\n```\r\n");

        Assert.Equal("x = 1", Assert.Single(split.Blocks).Code);
        Assert.Equal("Nothing:\nDone\n[proposed code: 1 lines]", split.Explanation);
    }

    [Fact]
    public void The_note_is_what_the_system_prompt_tells_the_model_never_to_write()
    {
        var split = AssistantAnswerParser.Split("```python\nx = 1\n```");

        Assert.Contains("[proposed code:", AssistantPrompts.System);
        Assert.Equal("[proposed code: 1 lines]", split.Explanation);
    }
}
