namespace TRVisionAI.Data.Entities;

public sealed class FrameEntity
{
    public long      Id           { get; set; }
    public int       SessionId    { get; set; }
    public long      FrameNumber  { get; set; }
    public DateTime  ReceivedAt   { get; set; }
    /// <summary>InspectionVerdict almacenado como int: -1=Unknown, 0=Ok, 1=Ng.</summary>
    public int       Verdict      { get; set; }
    public string    SolutionName { get; set; } = string.Empty;
    public long      TotalCount   { get; set; }
    public long      NgCount      { get; set; }
    public string    RawJson      { get; set; } = string.Empty;

    /// <summary>Path relativo al directorio raíz de imágenes.</summary>
    public string?   ImagePath     { get; set; }
    public string?   MaskImagePath { get; set; }

    /// <summary>Null = pendiente de envío a la API.</summary>
    public DateTime? ApiSentAt    { get; set; }

    public SessionEntity?            Session { get; set; }
    public ICollection<ModuleEntity> Modules { get; set; } = [];
}
