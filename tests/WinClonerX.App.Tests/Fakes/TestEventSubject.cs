using WinClonerX.Domain.Execution;

namespace WinClonerX.App.Tests.Fakes;

/// <summary>Observable that delivers each published event synchronously to the observers subscribed at that moment.</summary>
internal sealed class TestEventSubject : IObservable<ExecutionEvent>
{
    private readonly List<IObserver<ExecutionEvent>> _observers = [];

    public int ActiveObserverCount => _observers.Count;

    public IDisposable Subscribe(IObserver<ExecutionEvent> observer)
    {
        _observers.Add(observer);
        return new Unsubscriber(this, observer);
    }

    public void Publish(ExecutionEvent value)
    {
        foreach (var observer in _observers.ToArray())
        {
            observer.OnNext(value);
        }
    }

    private void Remove(IObserver<ExecutionEvent> observer) => _observers.Remove(observer);

    private sealed class Unsubscriber(TestEventSubject subject, IObserver<ExecutionEvent> observer) : IDisposable
    {
        public void Dispose() => subject.Remove(observer);
    }
}
