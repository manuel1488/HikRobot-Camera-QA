using TRVisionAI.Models;

namespace TRVisionAI.Desktop.ViewModels;

/// <summary>
/// DataGrid result row. Contains only lightweight presentation data.
/// Full detail (image, modules, JSON) is loaded on demand from the database.
/// </summary>
public sealed class ResultRow
{
    public int               RowNum       { get; init; }
    public DateTime          ReceivedAt   { get; init; }
    public InspectionVerdict Verdict      { get; init; }
    public string            SolutionName { get; init; } = string.Empty;
    public long              TotalCount   { get; init; }
    public long              NgCount      { get; init; }

    /// <summary>Key used to load full detail from the database on demand.</summary>
    public long FrameNumber { get; init; }
    public int  SessionId   { get; init; }

    public string VerdictText => Verdict switch
    {
        InspectionVerdict.Ok  => "OK",
        InspectionVerdict.Ng  => "NG",
        _                     => "?",
    };
}
