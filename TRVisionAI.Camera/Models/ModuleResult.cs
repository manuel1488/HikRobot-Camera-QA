namespace TRVisionAI.Models;

/// <summary>
/// Resultado de un módulo de inspección individual dentro de un frame.
/// </summary>
public sealed class ModuleResult
{
    public string           ModuleName { get; init; } = string.Empty;
    public InspectionVerdict Verdict   { get; init; }
    /// <summary>JSON en crudo del nodo del módulo (para almacenar detalle completo).</summary>
    public string           RawJson   { get; init; } = string.Empty;
}
