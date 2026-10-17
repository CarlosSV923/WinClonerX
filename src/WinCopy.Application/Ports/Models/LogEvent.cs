namespace WinCopy.Application.Ports.Models;

/// <summary>Evento del registro de operación: nombre del evento, propiedades y nivel.</summary>
/// <remarks>
/// El nombre no puede estar vacío y ninguna propiedad puede usar un nombre reservado (<see cref="ReservedPropertyNames"/>):
/// esas claves pertenecen a la cabecera que escribe el adaptador de registro en cada línea.
/// </remarks>
public sealed record LogEvent
{
    /// <summary>Crea el evento y lo valida.</summary>
    /// <exception cref="ArgumentException">El nombre está vacío o una propiedad usa un nombre reservado.</exception>
    /// <exception cref="ArgumentNullException">Las propiedades son nulas.</exception>
    public LogEvent(string @event, IReadOnlyDictionary<string, object?> properties, LogLevelKind level = LogLevelKind.Information)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@event);
        ArgumentNullException.ThrowIfNull(properties);

        foreach (var key in properties.Keys)
        {
            if (ReservedPropertyNames.Contains(key))
            {
                throw new ArgumentException($"Property name '{key}' is reserved by the log envelope.", nameof(properties));
            }
        }

        Event = @event;
        Properties = properties;
        Level = level;
    }

    /// <summary>Nombre del evento, por ejemplo <c>PhaseStarted</c>.</summary>
    public string Event { get; }

    /// <summary>Propiedades estructuradas del evento. Ninguna usa un nombre de <see cref="ReservedPropertyNames"/>.</summary>
    public IReadOnlyDictionary<string, object?> Properties { get; }

    /// <summary>Nivel de severidad del evento.</summary>
    public LogLevelKind Level { get; }

    /// <summary>
    /// Nombres de propiedad reservados. El adaptador de registro los escribe en la cabecera (envolvente) de cada línea, así que
    /// un evento no puede usarlos como propiedades. Es la única fuente de verdad: la validación de este registro y el adaptador
    /// de Serilog usan esta lista.
    /// </summary>
    public static IReadOnlyList<string> ReservedPropertyNames { get; } = ["Event", "ExecutionId", "AppVersion"];
}
