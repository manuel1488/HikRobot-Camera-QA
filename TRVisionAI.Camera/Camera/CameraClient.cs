using System.Runtime.InteropServices;
using TRVisionAI.Models;
using MvVSControlSDKNet;

namespace TRVisionAI.Camera;

/// <summary>
/// Wrapper around the Hikrobot SDK (MvVSControlSDKNet).
/// Encapsulates device lifecycle and frame acquisition.
/// Not thread-safe: use a dedicated thread for the GetResultData loop.
/// </summary>
public sealed class CameraClient : IDisposable
{
    private readonly CDevice _device  = new();
    private          CStream? _stream;
    private          bool     _loggedIn;
    private          bool     _running;
    private          bool     _disposed;

    // -------------------------------------------------------------------------
    // Connection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enumerates cameras available on the local GigE network.
    /// </summary>
    public static List<CameraInfo> EnumerateDevices()
    {
        var devList = new MV_VS_DEVICE_INFO_LIST();
        int ret = CSystem.EnumDevices(ref devList);

        if (ret != CErrorCode.MV_VS_OK)
            throw new HikrobotException("EnumDevices failed", ret);

        var result = new List<CameraInfo>((int)devList.nDeviceNum);
        for (uint i = 0; i < devList.nDeviceNum; i++)
        {
            if (devList.pDeviceInfo[i] == IntPtr.Zero) continue;
            var info = (MV_VS_DEVICE_INFO)Marshal.PtrToStructure(
                devList.pDeviceInfo[i], typeof(MV_VS_DEVICE_INFO))!;
            result.Add(CameraInfo.FromSdkInfo(i, info));
        }

        return result;
    }

    /// <summary>
    /// Connects and authenticates against the camera.
    /// </summary>
    /// <param name="encryptPassword">
    /// false (default) — plain-text password.<br/>
    /// true — password is already pre-encrypted (MD5 hex) by the caller.
    /// Use <see cref="PasswordHelper.ToMd5Hex"/> to generate the hash.
    /// </param>
    public void Connect(CameraInfo camera, string user, string password, bool encryptPassword = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sdkInfo = camera.SdkInfo;
        int ret = _device.CreateHandle(ref sdkInfo);
        if (ret != CErrorCode.MV_VS_OK)
            throw new HikrobotException("CreateHandle failed", ret);

        ret = _device.LoginEX(user, password, encryptPassword);
        if (ret != CErrorCode.MV_VS_OK)
        {
            string detail = _device.GetErrDescription((uint)ret, MV_VS_LANGUAGE.MVVS_LAGUAGE_ENGLISH);
            _device.DestroyHandle();
            throw new HikrobotException($"LoginEX(encrypt={encryptPassword}) failed — {detail}", ret);
        }

        _loggedIn = true;
    }

    // -------------------------------------------------------------------------
    // Acquisition
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures acquisition parameters and starts the frame stream.
    /// Must be called after Connect().
    /// </summary>
    public void StartAcquisition()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_loggedIn) throw new InvalidOperationException("Not connected.");

        var param = new CParam(_device);

        ThrowIfError(param.SetBoolValue("CommandImageMode", false), "Set CommandImageMode");
        ThrowIfError(param.SetEnumValue("AcquisitionMode", 2),      "Set AcquisitionMode");
        ThrowIfError(param.SetIntValue("ModuleID", 0),               "Set ModuleID");

        _stream = new CStream(_device);
        ThrowIfError(_stream.StartRun(), "StartRun");
        _running = true;
    }

    /// <summary>
    /// Attempts to retrieve the next available frame.
    /// Blocks until a frame arrives or the timeout expires.
    /// Always releases the internal buffer, even on error or timeout.
    /// </summary>
    /// <returns>The frame, or null if the timeout expired.</returns>
    public InspectionFrame? TryGetFrame(int timeoutMs = 1000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stream is null || !_running) throw new InvalidOperationException("Acquisition not started.");

        var frameData = new MV_VS_DATA();
        int ret = _stream.GetResultData(ref frameData, (uint)timeoutMs);

        if (ret == CErrorCode.MV_VS_E_NODATA)
        {
            _stream.ReleaseResultData(ref frameData);
            return null;
        }

        if (ret != CErrorCode.MV_VS_OK)
        {
            _stream.ReleaseResultData(ref frameData);
            throw new HikrobotException("GetResultData failed", ret);
        }

        try
        {
            return BuildFrame(ref frameData);
        }
        finally
        {
            _stream.ReleaseResultData(ref frameData);
        }
    }

    /// <summary>
    /// Stops the frame stream.
    /// </summary>
    public void StopAcquisition()
    {
        if (_stream is null || !_running) return;
        _stream.StopRun();
        _running = false;
    }

    // -------------------------------------------------------------------------
    // Frame construction
    // -------------------------------------------------------------------------

    private static InspectionFrame BuildFrame(ref MV_VS_DATA frameData)
    {
        byte[]? imageBytes = null;
        if (frameData.pImageData != IntPtr.Zero && frameData.nImageLen > 0)
        {
            imageBytes = new byte[frameData.nImageLen];
            Marshal.Copy(frameData.pImageData, imageBytes, 0, (int)frameData.nImageLen);
        }

        string rawJson      = string.Empty;
        var    verdict      = InspectionVerdict.Unknown;
        string solutionName = string.Empty;
        long   totalCount   = 0;
        long   ngCount      = 0;
        byte[]? chunkData   = null;

        IReadOnlyList<ModuleResult> modules = [];
        byte[]? maskImageBytes = null;

        if (frameData.pChunkData != IntPtr.Zero && frameData.nChunkDataLen > 0)
        {
            chunkData = new byte[frameData.nChunkDataLen];
            Marshal.Copy(frameData.pChunkData, chunkData, 0, (int)frameData.nChunkDataLen);
            (rawJson, verdict, solutionName, totalCount, ngCount, modules, maskImageBytes) =
                ChunkParser.ParseResult(chunkData, frameData.nChunkDataLen);
        }

        var debug = BuildDebugInfo(ref frameData, chunkData);

        return new InspectionFrame
        {
            FrameNumber    = frameData.nImageNum,
            ReceivedAt     = DateTime.Now,
            Verdict        = verdict,
            SolutionName   = solutionName,
            TotalCount     = totalCount,
            NgCount        = ngCount,
            RawJson        = rawJson,
            ImageBytes     = imageBytes,
            ImageWidth     = (int)frameData.nImageWidth,
            ImageHeight    = (int)frameData.nImageHeight,
            MaskImageBytes = maskImageBytes,
            ModuleResults  = modules,
            Debug          = debug,
        };
    }

    private static FrameDebugInfo BuildDebugInfo(ref MV_VS_DATA frameData, byte[]? chunkData)
    {
        string hexHead = string.Empty, hexTail = string.Empty, asText = string.Empty;

        if (chunkData is { Length: > 0 })
        {
            int head = Math.Min(64, chunkData.Length);
            int tail = Math.Min(64, chunkData.Length);
            hexHead = Convert.ToHexString(chunkData, 0, head);
            hexTail = Convert.ToHexString(chunkData, chunkData.Length - tail, tail);
            asText  = System.Text.Encoding.UTF8.GetString(chunkData).Replace("\0", "·");
            if (asText.Length > 512) asText = asText[..512] + "…";
        }

        return new FrameDebugInfo
        {
            ImageLen     = frameData.nImageLen,
            ChunkDataLen = frameData.nChunkDataLen,
            HasImagePtr  = frameData.pImageData != IntPtr.Zero,
            HasChunkPtr  = frameData.pChunkData != IntPtr.Zero,
            ChunkHexDump = hexHead,
            ChunkHexTail = hexTail,
            ChunkAsText  = asText,
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void ThrowIfError(int ret, string operation)
    {
        if (ret != CErrorCode.MV_VS_OK)
            throw new HikrobotException($"{operation} failed", ret);
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { StopAcquisition(); } catch { /* best effort */ }

        if (_loggedIn)
        {
            _device.Logout();
            _loggedIn = false;
        }

        _device.DestroyHandle();
    }
}
