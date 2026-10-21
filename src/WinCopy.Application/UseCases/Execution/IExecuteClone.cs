using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;

namespace WinCopy.Application.UseCases.Execution;

/// <summary>Ejecuta un plan confirmado de clonado: revalida, copia, escribe el diseño, repara el arranque y verifica.</summary>
public interface IExecuteClone
{
    /// <summary>Hechos de la ejecución en curso, en orden. Cada suscriptor recibe los eventos publicados después de suscribirse.</summary>
    IObservable<ExecutionEvent> Events { get; }

    /// <summary>
    /// Ejecuta el plan. Devuelve el informe final, también cuando la ejecución falla o se cancela. Lanza
    /// <see cref="InvalidOperationException"/> solo si el plan no existe o no está confirmado, antes de tocar nada.
    /// </summary>
    Task<CloneResultReport> ExecuteAsync(PlanId planId, CancellationToken ct);

    /// <summary>
    /// Solicita la cancelación de la ejecución en curso (FR-024). Sin efecto si no hay ejecución en marcha, si ya se ha pedido
    /// o si la verificación estructural ha pasado. Durante una fase no interrumpible, la cancelación se hace efectiva al terminar
    /// esa fase, antes de la siguiente. El token de <see cref="ExecuteAsync"/> tiene el mismo efecto.
    /// </summary>
    void RequestCancel();
}
