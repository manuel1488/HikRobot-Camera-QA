namespace Hikrobot.Models;

public sealed class InspectionFrame
{
    public ulong    FrameNumber  { get; init; }
    public DateTime ReceivedAt   { get; init; }
    public InspectionVerdict Verdict { get; init; }

    public string SolutionName { get; init; } = string.Empty;
    public long   TotalCount   { get; init; }
    public long   NgCount      { get; init; }

    /// <summary>JSON en crudo del Chunk Data (para diagnóstico).</summary>
    public string RawJson { get; init; } = string.Empty;

    /// <summary>Bytes JPEG de la imagen principal. Null si no se pudo extraer.</summary>
    public byte[]? ImageBytes  { get; init; }
    public int     ImageWidth  { get; init; }
    public int     ImageHeight { get; init; }

    /// <summary>Valores crudos del SDK para depuración paso a paso.</summary>
    public FrameDebugInfo Debug { get; init; } = new();
}

public enum InspectionVerdict
{
    Unknown = -1,
    Ok      =  0,
    Ng      =  1,
}

public sealed class FrameDebugInfo
{
    public uint   ImageLen      { get; init; }
    public uint   ChunkDataLen  { get; init; }
    public bool   HasImagePtr   { get; init; }
    public bool   HasChunkPtr   { get; init; }
    /// <summary>Primeros 64 bytes del ChunkData en hex.</summary>
    public string ChunkHexDump  { get; init; } = string.Empty;
    /// <summary>Últimos 64 bytes del ChunkData en hex (donde está el trailer de cada chunk).</summary>
    public string ChunkHexTail  { get; init; } = string.Empty;
    /// <summary>Intento de leer el ChunkData completo como UTF-8 (por si es JSON plano).</summary>
    public string ChunkAsText   { get; init; } = string.Empty;
}
