namespace WinCopy.Application.Errors;

/// <summary>Error de plataforma traducido a un código estable. El detalle técnico solo se muestra en el registro.</summary>
public sealed class PlatformException(PlatformErrorCode code, string technicalDetail, Exception? inner = null)
    : Exception(technicalDetail, inner)
{
    /// <summary>Código estable y localizable del error.</summary>
    public PlatformErrorCode Code { get; } = code;

    /// <summary>Detalle técnico para el registro; no se presenta a la persona.</summary>
    public string TechnicalDetail { get; } = technicalDetail;
}
