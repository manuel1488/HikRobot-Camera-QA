using TRVisionAI.Data.Entities;

namespace TRVisionAI.Data;

/// <summary>
/// Full frame detail loaded on demand for display in the UI.
/// </summary>
public sealed class FrameDetail
{
    public FrameEntity Entity     { get; }
    public byte[]?     ImageBytes { get; }
    public byte[]?     MaskBytes  { get; }

    public FrameDetail(FrameEntity entity, byte[]? imageBytes, byte[]? maskBytes)
    {
        Entity     = entity;
        ImageBytes = imageBytes;
        MaskBytes  = maskBytes;
    }
}
