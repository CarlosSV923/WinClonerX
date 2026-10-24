using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Shouldly;
using WinClonerX.App.Localization;
using Xunit;

namespace WinClonerX.App.Tests.Localization;

public sealed class ResourceParityTests
{
    private const string BaseName = "WinClonerX.App.Resources.Strings";

    private static readonly ResourceManager ResourceManager = new(BaseName, typeof(Loc).Assembly);

    private static readonly CultureInfo Spanish = new("es");

    private static readonly string[] RequiredKeys =
    [
        "App_Title",
        "Wizard_Step_Disks",
        "Wizard_Step_Review",
        "Wizard_Step_Confirm",
        "Wizard_Step_Progress",
        "Wizard_Step_Result",
        "Common_Back",
        "Common_Next",
        "Common_Cancel",
        "Result_Finish",
    ];

    [Fact]
    public void SpanishAndEnglishHaveTheSameKeys()
    {
        var english = ReadStrings(CultureInfo.InvariantCulture);
        var spanish = ReadStrings(Spanish);

        english.Keys.Except(spanish.Keys).Order().ShouldBeEmpty("keys present in Strings.resx but missing from Strings.es.resx");
        spanish.Keys.Except(english.Keys).Order().ShouldBeEmpty("keys present in Strings.es.resx but missing from Strings.resx");
    }

    [Fact]
    public void RequiredKeysExistInBothLanguages()
    {
        var english = ReadStrings(CultureInfo.InvariantCulture);
        var spanish = ReadStrings(Spanish);

        RequiredKeys.Except(english.Keys).ShouldBeEmpty("required keys missing from Strings.resx");
        RequiredKeys.Except(spanish.Keys).ShouldBeEmpty("required keys missing from Strings.es.resx");
    }

    [Fact]
    public void EnglishValuesAreNeverBlank()
    {
        AssertNoBlankValues(CultureInfo.InvariantCulture);
    }

    [Fact]
    public void SpanishValuesAreNeverBlank()
    {
        AssertNoBlankValues(Spanish);
    }

    [Fact]
    public void SpanishAndEnglishValuesHaveTheSameFormatPlaceholders()
    {
        var english = ReadStrings(CultureInfo.InvariantCulture);
        var spanish = ReadStrings(Spanish);
        var mismatches = english.Keys
            .Where(key => !Placeholders(english[key]).SequenceEqual(Placeholders(spanish[key])))
            .Select(key => $"{key}: en=[{string.Join(",", Placeholders(english[key]))}], es=[{string.Join(",", Placeholders(spanish[key]))}]")
            .Order()
            .ToList();

        mismatches.ShouldBeEmpty(string.Join(Environment.NewLine, mismatches));
    }

    [Theory]
    [InlineData("es-ES", "Wizard_Step_Disks", "Discos")]
    [InlineData("es-ES", "Common_Cancel", "Cancelar")]
    [InlineData("en-US", "Wizard_Step_Disks", "Disks")]
    [InlineData("en-US", "Common_Cancel", "Cancel")]
    [InlineData("fr-FR", "Common_Cancel", "Cancel")]
    [InlineData("fr-FR", "Wizard_Step_Review", "Review")]
    public void LocGetUsesCurrentUICultureWithEnglishFallback(string culture, string key, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            Loc.Get(key).ShouldBe(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void LocGetReturnsVisibleMarkerForMissingKey()
    {
        Loc.Get("Does_Not_Exist").ShouldBe("[[Does_Not_Exist]]");
    }

    private static void AssertNoBlankValues(CultureInfo culture)
    {
        var blank = ReadStrings(culture)
            .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key)
            .Order()
            .ToList();

        blank.ShouldBeEmpty($"blank values in culture '{culture.DisplayName}': {string.Join(", ", blank)}");
    }

    /// <summary>
    /// Reads the resource set for exactly this culture. Parents are not consulted, so a missing
    /// translation shows up as a key-set difference instead of being silently filled from English.
    /// </summary>
    private static Dictionary<string, string> ReadStrings(CultureInfo culture)
    {
        var set = ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resource set found for culture '{culture.Name}'.");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in set)
        {
            values[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }

        return values;
    }

    private static string[] Placeholders(string value) => Regex.Matches(value, @"\{(\d+)(?:,[^}]*)?\}")
        .Select(match => match.Groups[1].Value)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
