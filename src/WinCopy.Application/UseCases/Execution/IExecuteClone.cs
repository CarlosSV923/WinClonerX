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
}
