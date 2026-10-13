namespace WinCopy.Application.Ports;

/// <summary>Fuente de tiempo inyectable, para que las pruebas controlen el instante actual.</summary>
public interface IClock
{
    /// <summary>Instante actual en UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
