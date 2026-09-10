using WindowsSimpleTaskTabBar.Core.Update;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class ReleaseVersionTests
{
    // ---------------------------------------------------------------
    // Reading a version
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("v0.3.0")]
    [InlineData("V0.3.0")]
    [InlineData("0.3.0")]
    [InlineData("  v0.3.0  ")]
    public void ATagIsReadWithOrWithoutItsLeadingV(string text)
    {
        // Both shapes arrive in the same comparison: a release tag carries the v, and so does the
        // informational version of a published build, while a branch build's does not.
        Assert.Equal(new Version(0, 3, 0, 0), ReleaseVersion.Parse(text));
    }

    [Fact]
    public void AVersionWithFewerPartsIsTheSameAsOneWrittenWithZeros()
    {
        // An unspecified part of a System.Version is -1 rather than 0, so without normalizing
        // this to four parts, 0.3 would compare as older than 0.3.0 and never be offered.
        Assert.Equal(ReleaseVersion.Parse("0.3.0"), ReleaseVersion.Parse("0.3"));
    }

    [Fact]
    public void AFourPartAssemblyVersionIsRead()
    {
        Assert.Equal(new Version(0, 3, 0, 0), ReleaseVersion.Parse("0.3.0.0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("latest")]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("-1.2.3")]
    [InlineData("1..3")]
    public void TextThatIsNotAVersionIsNotOne(string text)
    {
        Assert.Null(ReleaseVersion.Parse(text));
    }

    [Theory]
    [InlineData("v1.2.3-beta")]
    [InlineData("v1.2.3+build.5")]
    public void APreReleaseOrBuildSuffixIsRefused(string text)
    {
        // /releases/latest already skips a pre-release, so a tag carrying one is an answer this
        // code did not expect. Trimming the suffix off is how a beta reaches everybody.
        Assert.Null(ReleaseVersion.Parse(text));
    }

    [Fact]
    public void AnAbsurdlyLongTagIsRefused()
    {
        // The tag reaches a notification, so its length is not left to whatever answered.
        Assert.Null(ReleaseVersion.Parse("v" + new string('9', 200)));
    }

    // ---------------------------------------------------------------
    // Comparing two releases
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("v0.4.0", "0.3.0")]
    [InlineData("v0.3.1", "0.3.0")]
    [InlineData("v1.0.0", "v0.9.9")]
    [InlineData("v0.4", "0.3.0")]
    public void ALaterReleaseIsNewer(string latest, string running)
    {
        Assert.True(ReleaseVersion.IsNewer(latest, running));
    }

    [Theory]
    [InlineData("v0.3.0", "0.3.0")]
    [InlineData("v0.3.0", "v0.3.0")]
    [InlineData("v0.2.0", "0.3.0")]
    [InlineData("v0.3.0", "0.3.0.0")]
    [InlineData("v0.3.0", "0.3")]
    public void AReleaseThatIsNotLaterIsNotNewer(string latest, string running)
    {
        // The last case is the one worth stating: a version written with fewer parts names the
        // same release, so it must not be offered as an update. Anything else would show a notice
        // that leads to the release already running.
        Assert.False(ReleaseVersion.IsNewer(latest, running));
    }

    [Theory]
    [InlineData("nonsense", "0.3.0")]
    [InlineData("v9.9.9", "nonsense")]
    [InlineData(null, null)]
    public void AnUnreadableVersionOnEitherSideNeverOffersAnUpdate(string latest, string running)
    {
        // Every failure on this path is silent, so a doubt has to mean no notice rather than a
        // notice the user cannot act on.
        Assert.False(ReleaseVersion.IsNewer(latest, running));
    }

    // ---------------------------------------------------------------
    // Recognizing a release the user has already been told about
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("v0.4.0", "0.4.0")]
    [InlineData("v0.4.0", "v0.4.0")]
    [InlineData("v0.4", "0.4.0")]
    public void TwoTagsForOneReleaseAreTheSameRelease(string a, string b)
    {
        // Comparing the strings would announce a release a second time to anyone whose settings
        // file happened to hold it written the other way.
        Assert.True(ReleaseVersion.IsSameRelease(a, b));
    }

    [Theory]
    [InlineData("v0.4.0", "v0.4.1")]
    [InlineData("v0.4.0", "")]
    [InlineData("v0.4.0", null)]
    public void TagsForDifferentReleasesAreNotTheSameRelease(string a, string b)
    {
        Assert.False(ReleaseVersion.IsSameRelease(a, b));
    }
}
