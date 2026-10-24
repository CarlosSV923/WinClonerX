using System.Globalization;

namespace WinClonerX.App.Tests.Fakes;

/// <summary>
/// Sets the current culture and UI culture to the named culture for the lifetime of the scope, and restores the previous
/// ones on dispose. Used where the test checks text for a specific language.
/// </summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string name)
    {
        var culture = new CultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUiCulture;
    }
}
