namespace WinCopy.Domain.Planning;

/// <summary>Forma de ajustar el origen al destino.</summary>
public enum SizeStrategy
{
    /// <summary>El destino es igual o mayor que el origen (versión 1).</summary>
    SameOrLarger,

    /// <summary>Hay que reducir la partición de Windows del origen antes de clonar (versión 2).</summary>
    ShrinkSource,
}
