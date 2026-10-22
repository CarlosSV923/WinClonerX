namespace WinClonerX.Application.UseCases.Planning;

/// <summary>Lee el inventario de discos y evalúa las comprobaciones previas y la elegibilidad de los destinos (FR-001 a FR-006).</summary>
public interface IGetInventoryAndEligibility
{
    /// <summary>Captura el inventario y los hechos del entorno, los evalúa y registra el resultado en el log.</summary>
    Task<InventoryOverview> ExecuteAsync(CancellationToken ct);
}
