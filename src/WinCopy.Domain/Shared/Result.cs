using System.Diagnostics.CodeAnalysis;

namespace WinCopy.Domain.Shared;

/// <summary>Resultado inmutable de una operación: un valor si tiene éxito o un error de dominio si falla.</summary>
[SuppressMessage("Design", "CA1000:No declarar miembros estáticos en tipos genéricos",
    Justification = "El contrato de Result exige las fábricas estáticas Success y Failure.")]
public readonly record struct Result<T, TError>
{
    private readonly bool _isSuccess;
    private readonly T? _value;
    private readonly TError? _error;

    private Result(bool isSuccess, T? value, TError? error)
    {
        _isSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    /// <summary>Indica si la operación ha tenido éxito.</summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>Valor del éxito. Lanza una excepción si el resultado es un fallo.</summary>
    public T Value => _isSuccess
        ? _value!
        : throw new InvalidOperationException("El resultado es un fallo y no tiene valor.");

    /// <summary>Error del fallo. Lanza una excepción si el resultado es un éxito.</summary>
    public TError Error => _isSuccess
        ? throw new InvalidOperationException("El resultado es un éxito y no tiene error.")
        : _error!;

    /// <summary>Crea un resultado de éxito con el valor indicado.</summary>
    public static Result<T, TError> Success(T value) => new(true, value, default);

    /// <summary>Crea un resultado de fallo con el error indicado.</summary>
    public static Result<T, TError> Failure(TError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, default, error);
    }

    /// <summary>Ejecuta la rama que corresponde al resultado y devuelve su salida.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<TError, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return _isSuccess ? onSuccess(_value!) : onFailure(_error!);
    }
}
