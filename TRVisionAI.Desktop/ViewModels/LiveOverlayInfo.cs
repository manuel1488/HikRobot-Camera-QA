namespace TRVisionAI.Desktop.ViewModels;

/// <summary>
/// Datos de overlay extraídos del último frame en vivo:
/// tipo/score del módulo de inspección y coordenadas del punto de detección.
/// </summary>
public sealed record LiveOverlayInfo(
    bool   IsOk,
    float  Score,
    string RangeText,
    float? CenterX,
    float? CenterY);
