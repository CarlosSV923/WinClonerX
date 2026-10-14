using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>
/// Decisión de diseño del destino: estrategia, una extensión por partición de origen (en su mismo orden) y, solo con
/// <see cref="SizeStrategy.ShrinkSource"/>, la reducción de la partición de Windows del origen.
/// </summary>
public sealed record LayoutDecision(SizeStrategy Strategy, IReadOnlyList<Extent> Extents, SourceShrinkStep? SourceShrink);
