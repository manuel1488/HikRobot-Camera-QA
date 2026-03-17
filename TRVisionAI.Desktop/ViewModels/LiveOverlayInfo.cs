namespace TRVisionAI.Desktop.ViewModels;

/// <summary>
/// Overlay data extracted from the latest live frame:
/// verdict/score from the inspection module and coordinates of the detection point.
/// </summary>
public sealed record LiveOverlayInfo(
    bool   IsOk,
    float  Score,
    string RangeText,
    float? CenterX,
    float? CenterY);
