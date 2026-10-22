namespace WinCopy.App.Services;

/// <summary>Controls the lifetime of the desktop application.</summary>
public interface IAppLifetime
{
    /// <summary>Shuts down the application.</summary>
    void Shutdown();
}
