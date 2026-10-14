using System.Globalization;

namespace WinCopy.App.Tests.Fakes;

/// <summary>
/// Sets the current culture and UI culture to English for the lifetime of the scope, so assertions on formatted text do not
/// depend on the machine's language. Restores the previous cultures on dispose.
/// </summary>
internal sealed class EnglishCulture : IDisposable
{
    private static readonly CultureInfo English = new("en-US");

    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

    public EnglishCulture()
    {
        CultureInfo.CurrentCulture = English;
        CultureInfo.CurrentUICulture = English;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUiCulture;
    }
}
