namespace TRVisionAI.Data.Entities;

public sealed class SessionEntity
{
    public int       Id          { get; set; }
    public DateTime  StartedAt   { get; set; }
    public DateTime? EndedAt     { get; set; }
    public string    CameraIp    { get; set; } = string.Empty;
    public string    CameraModel { get; set; } = string.Empty;
    public string    Operator    { get; set; } = string.Empty;

    /// <summary>Contadores denormalizados para resúmenes rápidos.</summary>
    public int OkCount { get; set; }
    public int NgCount { get; set; }

    public ICollection<FrameEntity> Frames { get; set; } = [];
}
