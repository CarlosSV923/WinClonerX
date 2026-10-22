using WinClonerX.Application.UseCases.Recovery;

namespace WinClonerX.App.Tests.Fakes;

/// <summary>
/// Fake de IRecoverPendingSourceRestore: devuelve el resultado configurado. Si <see cref="Gate"/> no es null, la llamada espera a
/// esa señal antes de terminar, para probar el estado ocupado. Si <see cref="Failure"/> no es null, la tarea falla con ella.
/// </summary>
internal sealed class FakeRecoverPendingSourceRestore : IRecoverPendingSourceRestore
{
    /// <summary>Resultado que devuelve RecoverAsync.</summary>
    public PendingSourceRestoreRecovery Result { get; set; } = PendingSourceRestoreRecovery.Nothing;

    /// <summary>Excepción con la que falla la tarea, o null.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Señal que la llamada espera antes de terminar, o null si no espera.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Número de llamadas a RecoverAsync.</summary>
    public int CallCount { get; private set; }

    /// <summary>Acción que se ejecuta al empezar cada llamada, para comprobar el orden respecto a otras lecturas.</summary>
    public Action? OnCall { get; set; }

    public async Task<PendingSourceRestoreRecovery> RecoverAsync(CancellationToken ct)
    {
        CallCount++;
        OnCall?.Invoke();

        if (Gate is not null)
        {
            await Gate.Task;
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        return Result;
    }
}
