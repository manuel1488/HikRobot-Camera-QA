namespace Hikrobot.Camera;

public sealed class HikrobotException : Exception
{
    public int ErrorCode { get; }

    public HikrobotException(string message, int errorCode)
        : base($"{message} (0x{(uint)errorCode:X8})")
    {
        ErrorCode = errorCode;
    }
}
