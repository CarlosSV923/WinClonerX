namespace WinClonerX.Application.Tests.Fakes;

/// <summary>Sujeto mínimo de IObservable para los fakes, sin dependencia de Rx.</summary>
public sealed class SimpleSubject<T> : IObservable<T>
{
    private readonly object _gate = new();
    private readonly List<IObserver<T>> _observers = [];

    /// <summary>Número de suscriptores activos.</summary>
    public int SubscriberCount
    {
        get
        {
            lock (_gate)
            {
                return _observers.Count;
            }
        }
    }

    /// <summary>Suscribe un observador; disponer el resultado lo cancela.</summary>
    public IDisposable Subscribe(IObserver<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            _observers.Add(observer);
        }

        return new Unsubscriber(this, observer);
    }

    /// <summary>Entrega un valor a todos los suscriptores activos.</summary>
    public void OnNext(T value)
    {
        IObserver<T>[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _observers];
        }

        foreach (var observer in snapshot)
        {
            observer.OnNext(value);
        }
    }

    private void Remove(IObserver<T> observer)
    {
        lock (_gate)
        {
            _observers.Remove(observer);
        }
    }

    private sealed class Unsubscriber(SimpleSubject<T> subject, IObserver<T> observer) : IDisposable
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
