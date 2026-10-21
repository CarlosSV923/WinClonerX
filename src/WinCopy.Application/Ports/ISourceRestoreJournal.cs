using WinCopy.Application.Ports.Models;

namespace WinCopy.Application.Ports;

/// <summary>
/// Diario de la reducción temporal del origen (FR-021a). Antes de reducir la partición de Windows se escribe una entrada con
/// su longitud original; cuando el origen vuelve a esa longitud, se borra. Si el proceso muere entre ambos pasos, la siguiente
/// ejecución lee la entrada y restaura la partición. La compensación en proceso no basta: no sobrevive a un corte de energía.
/// </summary>
public interface ISourceRestoreJournal
{
    /// <summary>
    /// Escribe la entrada de forma duradera (en disco, antes de volver) y sustituye cualquier entrada anterior. Si lanza
    /// excepción, la entrada no se ha escrito y el origen no debe reducirse.
    /// </summary>
    void Record(PendingSourceRestore entry);

    /// <summary>
    /// Lee el diario. Nunca lanza por un archivo dañado o ilegible: lo devuelve como <see cref="PendingSourceRestoreState.Invalid"/>.
    /// </summary>
    PendingSourceRestoreRead Read();

    /// <summary>Borra la entrada pendiente. Si no hay ninguna, no hace nada.</summary>
    void Clear();
}
