using TRVisionAI.Models;

namespace TRVisionAI.Desktop.ViewModels;

/// <summary>
/// Fila de resultado para el DataGrid. Solo contiene datos ligeros de presentación.
/// El detalle completo (imagen, módulos, JSON) se carga bajo demanda desde la BD.
/// </summary>
public sealed class ResultRow
{
    public int               RowNum       { get; init; }
    public DateTime          ReceivedAt   { get; init; }
    public InspectionVerdict Verdict      { get; init; }
    public string            SolutionName { get; init; } = string.Empty;
    public long              TotalCount   { get; init; }
    public long              NgCount      { get; init; }

    /// <summary>Clave para cargar el detalle desde la BD bajo demanda.</summary>
    public long FrameNumber { get; init; }
    public int  SessionId   { get; init; }

    public string VerdictText => Verdict switch
    {
        InspectionVerdict.Ok  => "OK",
        InspectionVerdict.Ng  => "NG",
        _                     => "?",
    };
}
