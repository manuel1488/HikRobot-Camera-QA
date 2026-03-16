namespace TRVisionAI.Data.Entities;

public sealed class ModuleEntity
{
    public long   Id         { get; set; }
    public long   FrameId    { get; set; }
    public string ModuleName { get; set; } = string.Empty;
    /// <summary>0=Ok, 1=Ng, -1=Unknown.</summary>
    public int    Verdict    { get; set; }
    public string RawJson    { get; set; } = string.Empty;

    public FrameEntity? Frame { get; set; }
}
