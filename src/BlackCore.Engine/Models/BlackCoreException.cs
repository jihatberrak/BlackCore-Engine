namespace BlackCore.Engine.Models;

/// <summary>Pipeline adımları tarafından hata kodu ile fırlatılan kontrollü hata.</summary>
public sealed class BlackCoreException : Exception
{
    public BlackCoreException(ErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public ErrorCode ErrorCode { get; }
}
