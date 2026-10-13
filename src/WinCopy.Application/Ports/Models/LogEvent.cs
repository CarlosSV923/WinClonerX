namespace WinCopy.Application.Ports.Models;

/// <summary>Evento del registro de operación: nombre del evento, propiedades y nivel.</summary>
public sealed record LogEvent(
    string Event,
    IReadOnlyDictionary<string, object?> Properties,
    LogLevelKind Level = LogLevelKind.Information);
