using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;

namespace WinClonerX.Application.Tests.Fakes;

/// <summary>
/// Fake de ISourceRestoreJournal con una sola entrada en memoria. Anota cuántas reducciones había hecho el resizer en el
/// momento de cada escritura, para comprobar que el diario se escribe antes de reducir.
/// </summary>
public sealed class FakeSourceRestoreJournal(FakeSourceResizer? resizer = null) : FakeBase, ISourceRestoreJournal
{
    /// <summary>Entrada guardada en el diario, o null si está vacío.</summary>
    public PendingSourceRestore? Entry { get; set; }

    /// <summary>Si no es null, Read devuelve el diario como dañado con este detalle.</summary>
    public string? Corruption { get; set; }

    /// <summary>Todas las entradas escritas, en orden.</summary>
    public List<PendingSourceRestore> Recorded { get; } = [];

    /// <summary>Reducciones (ShrinkAsync) que el resizer había recibido en el momento de cada escritura, en orden.</summary>
    public List<int> ShrinksBeforeEachRecord { get; } = [];

    /// <summary>Número de borrados realizados con éxito.</summary>
    public int Cleared { get; private set; }

    /// <summary>Restauraciones (RestoreAsync) que el resizer había recibido en el momento de cada borrado, en orden.</summary>
    public List<int> RestoresBeforeEachClear { get; } = [];

    /// <summary>Escribe la entrada. Si hay un fallo configurado, no la guarda.</summary>
    public void Record(PendingSourceRestore entry)
    {
        Enter(nameof(Record));
        ShrinksBeforeEachRecord.Add(resizer?.Calls.Count(call => call == nameof(ISourceResizer.ShrinkAsync)) ?? 0);
        Recorded.Add(entry);
        Entry = entry;
    }

    /// <summary>Devuelve la entrada, o Absent, o Invalid si hay corrupción configurada.</summary>
    public PendingSourceRestoreRead Read()
    {
        Enter(nameof(Read));

        if (Corruption is not null)
        {
            return PendingSourceRestoreRead.Invalid(Corruption);
        }

        return Entry is { } entry ? PendingSourceRestoreRead.Present(entry) : PendingSourceRestoreRead.Absent;
    }

    /// <summary>Borra la entrada. Si hay un fallo configurado, la entrada se conserva.</summary>
    public void Clear()
    {
        Enter(nameof(Clear));
        RestoresBeforeEachClear.Add(resizer?.Calls.Count(call => call == nameof(ISourceResizer.RestoreAsync)) ?? 0);
        Entry = null;
        Cleared++;
    }
}
