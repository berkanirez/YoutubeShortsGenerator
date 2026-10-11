namespace ShortsGenerator.Application.Tests;

public class StoryFormatsTests
{
    [Theory]
    [InlineData("Arm Levels Ranked - Which One Are You?", StoryFormat.Levels)]
    [InlineData("What Happens When You Dead Hang Every Day", StoryFormat.InsideTheBody)]
    [InlineData("3 Push-Up Mistakes Killing Your Gains", StoryFormat.Countdown)]
    [InlineData("Is Cardio Bad for Muscle Growth?", StoryFormat.MythStory)]
    [InlineData("Does Gym Stunt Your Growth?", StoryFormat.MythStory)]
    public void TheTopicChoosesTheShapeThatFitsIt(string topic, StoryFormat expected)
    {
        Assert.Equal(expected, StoryFormats.Pick(topic));
    }

    [Fact]
    public void OtherTopics_GetAStableGeneralFormat_AndVaryAcrossTopics()
    {
        string[] topics = ["Why Your Pull-Ups Aren't Increasing", "Build Bigger Forearms", "Train Your Neck", "How to Hold a Plank", "Squat Depth Explained", "Grip Strength Basics"];

        var picks = topics.Select(StoryFormats.Pick).ToList();

        Assert.Equal(picks, topics.Select(StoryFormats.Pick));   // deterministic
        Assert.All(picks, p => Assert.Contains(p, new[] { StoryFormat.Journey, StoryFormat.Countdown, StoryFormat.MythStory }));
        Assert.True(picks.Distinct().Count() > 1);
    }

    [Fact]
    public void ThePrompt_CarriesTheStoryShape_NextToTheHookRules()
    {
        var prompt = ScriptPromptBuilder.Build("3 Push-Up Mistakes Killing Your Gains", format: StoryFormat.Countdown);

        Assert.Contains("Make it a story, not a list of facts", prompt);
        Assert.Contains("number three, number two, number one", prompt);
        Assert.Contains("The FIRST sentence is the hook", prompt);
        Assert.Contains("never name real people, channels or brands", prompt);
    }
}

public class StoryFormatRotationTests
{
    [Theory]
    [InlineData("Number three: you skip the warm-up. Number two: ...", StoryFormat.Countdown)]
    [InlineData("This guy trained for months and nothing changed.", StoryFormat.Journey)]
    [InlineData("At level one you can hang. Level four is a one-arm hang.", StoryFormat.Levels)]
    [InlineData("Most people believe cardio kills gains.", StoryFormat.MythStory)]
    [InlineData("Plain facts about squats.", null)]
    public void Detect_RecognizesTheShapeOfAFinishedNarration(string script, StoryFormat? expected)
    {
        Assert.Equal(expected, StoryFormats.Detect(script));
    }

    [Fact]
    public void AGeneralTopic_GetsTheShapeUsedLeastRecently()
    {
        string[] recent = ["Number three: ... number two: ...", "This guy kept failing his pull-ups."];

        Assert.Equal(StoryFormat.MythStory, StoryFormats.Pick("Build Bigger Forearms", recent));
    }

    [Fact]
    public void ATopicThatNamesItsShape_KeepsIt_WhateverWasUsedBefore()
    {
        Assert.Equal(StoryFormat.Countdown, StoryFormats.Pick("3 Push-Up Mistakes", ["Number three: ..."]));
    }
}
