using Hikrobot.Models;

namespace HikrobotDesktop.ViewModels;

/// <summary>
/// Fila de resultado para el DataGrid.
/// </summary>
public sealed class ResultRow
{
    public int               RowNum       { get; init; }
    public DateTime          ReceivedAt   { get; init; }
    public InspectionVerdict Verdict      { get; init; }
    public string            SolutionName { get; init; } = string.Empty;
    public long              TotalCount   { get; init; }
    public long              NgCount      { get; init; }

    public string VerdictText => Verdict switch
    {
        InspectionVerdict.Ok  => "OK",
        InspectionVerdict.Ng  => "NG",
        _                     => "?",
    };
}
