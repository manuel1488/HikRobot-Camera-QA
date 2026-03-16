namespace HikrobotProbe.Camera;

public sealed class HikrobotException : Exception
{
    public int ErrorCode { get; }

    public HikrobotException(string operation, int errorCode)
        : base($"{operation} — SDK error code: 0x{errorCode:X8}")
    {
        ErrorCode = errorCode;
    }
}
