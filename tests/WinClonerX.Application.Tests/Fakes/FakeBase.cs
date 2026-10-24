namespace WinClonerX.Application.Tests.Fakes;

/// <summary>Base común de los fakes: registra las llamadas e inyecta fallos por nombre de método.</summary>
public abstract class FakeBase
{
    /// <summary>Registro de llamadas en orden, por nombre de método. Una llamada que falla también se registra.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Excepciones que se lanzan al invocar un método, indexadas por su nombre.</summary>
    public Dictionary<string, Exception> FailOn { get; } = new(StringComparer.Ordinal);

    /// <summary>Registra una llamada síncrona y lanza la excepción configurada, si la hay.</summary>
    protected void Enter(string method)
    {
        Calls.Add(method);
        if (FailOn.TryGetValue(method, out var failure))
        {
            throw failure;
        }
    }

    /// <summary>Registra una llamada asíncrona sin resultado; el fallo configurado sale por la Task, como en un método async real.</summary>
    protected Task Complete(string method, CancellationToken ct, Action? effect = null)
    {
        Calls.Add(method);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled(ct);
        }

        if (FailOn.TryGetValue(method, out var failure))
        {
            return Task.FromException(failure);
        }

        effect?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Registra una llamada asíncrona con resultado; el fallo configurado sale por la Task.</summary>
    protected Task<T> Reply<T>(string method, Func<T> result, CancellationToken ct)
    {
        Calls.Add(method);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(ct);
        }

        return FailOn.TryGetValue(method, out var failure)
            ? Task.FromException<T>(failure)
            : Task.FromResult(result());
    }

    /// <summary>Igual que Reply, para métodos que devuelven ValueTask.</summary>
    protected ValueTask<T> ReplyValue<T>(string method, Func<T> result, CancellationToken ct) =>
        new(Reply(method, result, ct));
}
