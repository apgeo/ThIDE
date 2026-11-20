// Captions as subtitle files: SubRip and WebVTT, one caption at a time.

namespace Therion.Blender.Tests;

public class SubtitlesTests
{
    private static CaptionSpec Caption(string text, double start, double end) => new() { Text = text, Start = start, End = end };

    [Fact]
    public void Srt_NumbersTheCues_InOrderOfTheirStart_WithCommasInTheTimes()
    {
        string srt = Subtitles.Srt([Caption("Sala Mare", 3725.5, 3730.25), Caption("Intrarea", 0, 2.5)]);

        Assert.Equal(
            "1\n00:00:00,000 --> 00:00:02,500\nIntrarea\n\n" +
            "2\n01:02:05,500 --> 01:02:10,250\nSala Mare\n\n", srt);
    }

    [Fact]
    public void Vtt_StartsWithItsHeader_HasDotsInTheTimes_AndKeepsMarkupOutOfTheText()
    {
        string vtt = Subtitles.Vtt([Caption("Puț <P40> & galerie", 1.0005, 4)]);

        Assert.Equal("WEBVTT\n\n00:00:01.001 --> 00:00:04.000\nPuț &lt;P40&gt; &amp; galerie\n", vtt);
        Assert.Equal("WEBVTT\n", Subtitles.Vtt([]));
        Assert.Equal("", Subtitles.Srt([]));
    }

    [Fact]
    public void ACaptionOfSeveralLines_KeepsThem_WithoutTheEmptyOnes()
    {
        string srt = Subtitles.Srt([Caption("first\r\n\r\n  second  \n", 0, 1)]);

        Assert.Equal("1\n00:00:00,000 --> 00:00:01,000\nfirst\nsecond\n\n", srt);
    }

    [Fact]
    public void WhereTwoCaptionsOverlap_TheLaterOneReplacesTheEarlier()
    {
        var shown = Subtitles.OneAtATime(
        [
            Caption("long", 0, 10), Caption("inside", 3, 5), Caption("after", 12, 14),
            Caption("same start, listed first", 20, 25), Caption("same start, listed later", 20, 22),
            Caption("", 30, 31), Caption("no time", 40, 40),
        ]);

        Assert.Equal(
            [("long", 0.0, 3.0), ("inside", 3.0, 5.0), ("after", 12.0, 14.0), ("same start, listed later", 20.0, 22.0)],
            shown.Select(c => (c.Text, c.Start, c.End)));
        // The files say what the movie shows.
        Assert.Contains("00:00:00,000 --> 00:00:03,000\nlong\n", Subtitles.Srt([Caption("long", 0, 10), Caption("inside", 3, 5)]));
    }

    [Fact]
    public void Times_AreWrittenTheSameUnderEveryCulture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ro-RO");
            Assert.Contains("00:00:01,500 --> 00:00:02,250", Subtitles.Srt([Caption("x", 1.5, 2.25)]));
            Assert.Contains("00:00:01.500 --> 00:00:02.250", Subtitles.Vtt([Caption("x", 1.5, 2.25)]));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }
}
