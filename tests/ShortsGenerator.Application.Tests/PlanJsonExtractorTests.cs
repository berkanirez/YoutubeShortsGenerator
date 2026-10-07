namespace ShortsGenerator.Application.Tests;

public class PlanJsonExtractorTests
{
    [Fact]
    public void Extract_PlainJson_ReturnsItUnchanged()
    {
        const string json = "{\"title\":\"Test\"}";

        var result = PlanJsonExtractor.Extract(json);

        Assert.Equal(json, result);
    }

    [Fact]
    public void Extract_StripsMarkdownCodeFence()
    {
        const string raw = "```json\n{\"title\":\"Test\"}\n```";

        var result = PlanJsonExtractor.Extract(raw);

        Assert.Equal("{\"title\":\"Test\"}", result);
    }

    [Fact]
    public void Extract_StripsSurroundingProse()
    {
        const string raw = "Here is the plan:\n{\"title\":\"Test\"}\nLet me know if you need changes!";

        var result = PlanJsonExtractor.Extract(raw);

        Assert.Equal("{\"title\":\"Test\"}", result);
    }

    [Fact]
    public void Extract_ConvertsStringNullLiteralToRealNull()
    {
        const string raw = "{\"animation\":\"null\",\"onScreenText\":\"null\",\"assetIds\":[]}";

        var result = PlanJsonExtractor.Extract(raw);

        Assert.Equal("{\"animation\":null,\"onScreenText\":null,\"assetIds\":[]}", result);
    }

    [Fact]
    public void Extract_DoesNotTouchNarrationThatLegitimatelyContainsTheWordNull()
    {
        const string raw = "{\"narration\":\"This exercise targets the null point of balance.\"}";

        var result = PlanJsonExtractor.Extract(raw);

        Assert.Equal(raw, result);
    }
}
