using MvVSControlSDKNet;
using System.Text;

namespace TRVisionAI.Camera;

/// <summary>
/// Information about a camera discovered on the network.
/// </summary>
public sealed class CameraInfo
{
    public uint   Index        { get; private init; }
    public string IpAddress    { get; private init; } = string.Empty;
    public string ModelName    { get; private init; } = string.Empty;
    public string SerialNumber { get; private init; } = string.Empty;
    public string UserName     { get; private init; } = string.Empty;

    // Internal reference to the SDK struct needed for CreateHandle
    internal MV_VS_DEVICE_INFO SdkInfo { get; private init; }

    internal static CameraInfo FromSdkInfo(uint index, MV_VS_DEVICE_INFO info)
    {
        uint ip = info.nCurrentIp;
        string ipStr = $"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";
        string userName = Encoding.UTF8.GetString(info.chUserDefinedName).TrimEnd('\0');

        return new CameraInfo
        {
            Index        = index,
            IpAddress    = ipStr,
            ModelName    = info.chModelName.TrimEnd('\0'),
            SerialNumber = info.chSerialNumber.TrimEnd('\0'),
            UserName     = userName,
            SdkInfo      = info,
        };
    }

    public override string ToString()
    {
        string display = string.IsNullOrWhiteSpace(UserName) ? ModelName : UserName;
        return $"[{Index}] {display}  IP: {IpAddress}  SN: {SerialNumber}";
    }
}
