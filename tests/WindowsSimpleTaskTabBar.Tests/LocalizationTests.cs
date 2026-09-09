using WindowsSimpleTaskTabBar.Core.Localization;
using Xunit;

namespace WindowsSimpleTaskTabBar.Tests;

public class LocalizationTests
{
    /// <summary>Every language that is offered, for the tests that check each one.</summary>
    public static IEnumerable<object[]> AllLanguages()
    {
        foreach (LanguageInfo language in Languages.All)
            yield return new object[] { language.Code };
    }

    [Fact]
    public void EnglishIsOffered()
    {
        // The source language, and what everything else falls back to.
        Assert.NotNull(Languages.Find(Languages.English));
    }

    [Fact]
    public void TheTwelveLanguagesAreOffered()
    {
        // The list of issue #35, in the order it gives them. A language dropped by accident
        // would otherwise only show up as a missing entry in the settings dialog.
        string[] expected =
        {
            "en", "ja", "zh-CN", "zh-TW", "ru", "de", "fr", "es", "pt-BR", "ko", "pl", "it",
        };

        Assert.Equal(expected, Languages.All.Select(language => language.Code));
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryLanguageOfferedHasATable(string code)
    {
        Assert.True(UiStrings.HasTable(code));
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryTableAnswersEveryName(string code)
    {
        // The parity check the whole arrangement rests on: a name added to StringId and left out
        // of a translation would otherwise show that language's users the English text.
        IReadOnlyDictionary<StringId, string> table = UiStrings.Table(code);

        foreach (StringId id in Enum.GetValues(typeof(StringId)))
            Assert.True(table.ContainsKey(id), code + " is missing " + id);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void NoTableHoldsAnEmptyEntry(string code)
    {
        foreach (KeyValuePair<StringId, string> entry in UiStrings.Table(code))
            Assert.False(string.IsNullOrWhiteSpace(entry.Value), code + " leaves " + entry.Key + " empty");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void NoTableHoldsAnEntryTheOthersDoNot(string code)
    {
        // The other half of parity: a name removed from StringId leaves an entry behind that
        // nothing reads, and a table with a name of its own would not be reachable at all.
        IReadOnlyDictionary<StringId, string> table = UiStrings.Table(code);

        Assert.Equal(Enum.GetValues(typeof(StringId)).Length, table.Count);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryTextThatTakesAValueKeepsItsPlaceholder(string code)
    {
        // A translation that dropped {0} would leave the version number, the bar height or the
        // group number out of what the user reads, and nothing else would notice.
        StringId[] takeAValue =
        {
            StringId.SettingsTitle,
            StringId.HeightStandard,
            StringId.HeightCompact,
            StringId.GroupsDefaultName,
            StringId.MenuUpdateAvailable,
            StringId.UpdateAvailableAsk,
            StringId.UpdateUpToDate,
            StringId.UpdateNotice,
        };

        IReadOnlyDictionary<StringId, string> table = UiStrings.Table(code);

        foreach (StringId id in takeAValue)
            Assert.Contains("{0}", table[id]);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryLanguageNamesAFont(string code)
    {
        Assert.False(string.IsNullOrWhiteSpace(Languages.FontFamilyFor(code)));
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryLanguageIsListedUnderItsOwnName(string code)
    {
        Assert.False(string.IsNullOrWhiteSpace(Languages.Find(code).NativeName));
    }

    [Fact]
    public void EachOfChineseJapaneseAndKoreanIsDrawnWithItsOwnFont()
    {
        // These three share code points, so one font cannot serve all three: it would give the
        // other two the letter shapes of its own language.
        Assert.Equal("Yu Gothic UI", Languages.FontFamilyFor("ja"));
        Assert.Equal("Microsoft YaHei UI", Languages.FontFamilyFor("zh-CN"));
        Assert.Equal("Microsoft JhengHei UI", Languages.FontFamilyFor("zh-TW"));
        Assert.Equal("Malgun Gothic", Languages.FontFamilyFor("ko"));
    }

    [Fact]
    public void ALanguageWithNoFontOfItsOwnGetsTheDefaultOne()
    {
        Assert.Equal("Segoe UI", Languages.FontFamilyFor(Languages.English));
        Assert.Equal("Segoe UI", Languages.FontFamilyFor("xx"));
    }

    // -----------------------------------------------------------------
    // Which language a Windows culture belongs to
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-SG", "zh-CN")]
    [InlineData("zh", "zh-CN")]
    [InlineData("zh-Hant", "zh-TW")]
    [InlineData("zh-TW", "zh-TW")]
    [InlineData("zh-HK", "zh-TW")]
    [InlineData("zh-MO", "zh-TW")]
    [InlineData("zh-Hant-HK", "zh-TW")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("de-AT", "de")]
    [InlineData("ja-JP", "ja")]
    [InlineData("en-GB", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void ACultureBelongsToOneLanguage(string culture, string expected)
    {
        Assert.Equal(expected, Languages.Canonical(culture));
    }

    [Fact]
    public void ACultureIsMatchedWhateverCaseItIsWrittenIn()
    {
        Assert.Equal("ja", Languages.Canonical("JA-JP"));
        Assert.Equal("zh-TW", Languages.Canonical("ZH-HANT"));
    }

    // -----------------------------------------------------------------
    // Which language the interface ends up in
    // -----------------------------------------------------------------

    [Fact]
    public void TheLanguageTheUserChoseWinsOverWindows()
    {
        Assert.Equal("ja", Languages.Resolve("ja", "en-US"));
        Assert.Equal("en", Languages.Resolve("en", "ja-JP"));
    }

    [Fact]
    public void WindowsDecidesWhenNoLanguageWasChosen()
    {
        Assert.Equal("ja", Languages.Resolve(Languages.Automatic, "ja-JP"));
        Assert.Equal("en", Languages.Resolve(Languages.Automatic, "en-GB"));
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-Hant", "zh-TW")]
    [InlineData("zh-HK", "zh-TW")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("ru-RU", "ru")]
    [InlineData("ko-KR", "ko")]
    public void ACultureReachesTheTableItBelongsTo(string culture, string expected)
    {
        Assert.Equal(expected, Languages.Resolve(Languages.Automatic, culture));
    }

    [Fact]
    public void ACultureWithNoTableFallsBackToEnglish()
    {
        // Twelve languages are offered; Windows has many more, and each of the rest reads
        // English rather than nothing.
        Assert.Equal("en", Languages.Resolve(Languages.Automatic, "sv-SE"));
        Assert.Equal("en", Languages.Resolve(Languages.Automatic, "th-TH"));
        Assert.Equal("en", Languages.Resolve(Languages.Automatic, "xx-YY"));
    }

    [Fact]
    public void ALanguageThatIsNoLongerOfferedIsTreatedAsAutomatic()
    {
        // A settings file can name a language this version does not have.
        Assert.Equal("ja", Languages.Resolve("xx", "ja-JP"));
    }

    [Fact]
    public void ALanguageWithARegionInItIsChosenByItsWholeCode()
    {
        // Two of the twelve are named with a region, and neither is reachable by its two-letter
        // code alone: pt is Brazilian here, and zh has to say which script it means.
        Assert.Equal("pt-BR", Languages.Resolve("pt-BR", "en-US"));
        Assert.Equal("zh-TW", Languages.Resolve("zh-TW", "en-US"));
    }

    // -----------------------------------------------------------------
    // Reading the text
    // -----------------------------------------------------------------

    [Fact]
    public void TextComesBackInTheLanguageAskedFor()
    {
        Assert.Equal("Exit", new UiText("en")[StringId.MenuExit]);
        Assert.Equal("終了", new UiText("ja")[StringId.MenuExit]);
        Assert.Equal("Beenden", new UiText("de")[StringId.MenuExit]);
        Assert.Equal("退出", new UiText("zh-CN")[StringId.MenuExit]);
    }

    [Fact]
    public void AnUnknownLanguageReadsEnglish()
    {
        var text = new UiText("xx");

        Assert.Equal(Languages.English, text.Language);
        Assert.Equal("Exit", text[StringId.MenuExit]);
    }

    [Fact]
    public void ALanguageIsMatchedWhateverCaseItIsWrittenIn()
    {
        Assert.Equal("ja", new UiText("JA").Language);
    }

    [Fact]
    public void ANumberIsFilledIntoTheTextThatLeavesRoomForIt()
    {
        Assert.Equal("Group 3", new UiText("en").Format(StringId.GroupsDefaultName, 3));
        Assert.Equal("グループ 3", new UiText("ja").Format(StringId.GroupsDefaultName, 3));
    }

    [Fact]
    public void EveryColourHasAName()
    {
        var text = new UiText("ja");

        for (int accent = 0; accent < 8; accent++)
            Assert.False(string.IsNullOrEmpty(text.AccentName(accent)));
    }

    [Fact]
    public void ANumberThatIsNotAColourHasNoName()
    {
        var text = new UiText("en");

        Assert.Equal(string.Empty, text.AccentName(-1));
        Assert.Equal(string.Empty, text.AccentName(8));
    }
}
