using WinCopy.Domain.Execution;

namespace WinCopy.Application.UseCases.Execution;

/// <summary>
/// Sujeto mínimo de <see cref="IObservable{T}"/> para los eventos de ejecución, sin dependencia de Rx. Entrega cada evento de
/// forma síncrona a los suscriptores activos en el momento de publicarlo.
/// </summary>
internal sealed class ExecutionEventSubject : IObservable<ExecutionEvent>
{
    private readonly object _gate = new();
    private readonly List<IObserver<ExecutionEvent>> _observers = [];

    /// <summary>Suscribe un observador. Disponer el resultado lo quita de la lista.</summary>
    public IDisposable Subscribe(IObserver<ExecutionEvent> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (_gate)
        {
            _observers.Add(observer);
        }

        return new Unsubscriber(this, observer);
    }

    /// <summary>Entrega el evento a todos los suscriptores activos.</summary>
    public void Publish(ExecutionEvent value)
    {
        IObserver<ExecutionEvent>[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _observers];
        }

        foreach (var observer in snapshot)
        {
            observer.OnNext(value);
        }
    }

    private void Remove(IObserver<ExecutionEvent> observer)
    {
        lock (_gate)
        {
            _observers.Remove(observer);
        }
    }

    private sealed class Unsubscriber(ExecutionEventSubject subject, IObserver<ExecutionEvent> observer) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                subject.Remove(observer);
            }
        }
    }
}
