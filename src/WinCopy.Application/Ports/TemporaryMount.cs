namespace WinCopy.Application.Ports;

/// <summary>Montaje temporal: Root es la raíz accesible y Release retira el punto de montaje al liberarse.</summary>
public sealed record TemporaryMount(string Root, IAsyncDisposable Release);
