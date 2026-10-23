namespace WinClonerX.Application.Ports.Models;

/// <summary>Una muestra de un tramo que difirió durante la verificación completa.</summary>
/// <param name="SourceOffset">Desplazamiento del tramo dentro del volumen de origen.</param>
/// <param name="Length">Longitud del tramo leído.</param>
/// <param name="DifferingBytes">Bytes distintos en la primera lectura.</param>
/// <param name="SourceStable">Indica si la segunda lectura del origen coincide con la primera.</param>
/// <param name="TargetStable">Indica si la segunda lectura del destino coincide con la primera.</param>
/// <param name="SecondReadDiffering">Bytes distintos entre la segunda lectura del origen y la del destino.</param>
/// <param name="TargetAllZero">Indica si los bytes distintos del destino son todos cero.</param>
/// <param name="SourceAllZero">Indica si los bytes distintos del origen son todos cero.</param>
/// <param name="Shift">Desplazamiento encontrado al buscar una coincidencia cercana en el origen.</param>
/// <param name="SourceSample">Hasta 16 bytes hexadecimales del origen desde la primera diferencia.</param>
/// <param name="TargetSample">Hasta 16 bytes hexadecimales del destino desde la primera diferencia.</param>
public sealed record ChunkMismatchSample(
    long SourceOffset,
    int Length,
    long DifferingBytes,
    bool SourceStable,
    bool TargetStable,
    long SecondReadDiffering,
    bool TargetAllZero,
    bool SourceAllZero,
    long? Shift,
    string SourceSample,
    string TargetSample);
