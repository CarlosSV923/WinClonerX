namespace WinClonerX.Application.Tests.Fakes;

/// <summary>Valores por defecto compartidos por los fakes.</summary>
public static class FakeDefaults
{
    /// <summary>Instante fijo usado como hora por defecto.</summary>
    public static DateTimeOffset Now { get; } = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
}
