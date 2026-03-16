using System.Text;
using System.Text.Json;
using Hikrobot.Models;

namespace Hikrobot.Camera;

/// <summary>
/// Parsea el Chunk Data de un frame MV_VS_DATA y extrae el resultado de inspección.
///
/// Estructura del buffer pChunkData (se lee de atrás hacia adelante):
///   [...datos del chunk...][ChunkID: 4B big-endian][ChunkLen: 4B big-endian]
///
/// Chunk IDs relevantes:
///   60005537 — resultado JSON (ScDeviceSolutionRunningResult, etc.)
///   60005536 — imagen de máscara del módulo
/// </summary>
internal static class ChunkParser
{
    private const uint ChunkResultPort    = 60005537;
    private const uint ChunkMaskImagePort = 60005536;

    public static (string rawJson, InspectionVerdict verdict, string solutionName, long total, long ng)
        ParseResult(byte[] chunkData, uint chunkDataLen)
    {
        uint   offset  = 0;
        var    endian  = new byte[4];
        string rawJson = string.Empty;

        while (chunkDataLen > offset)
        {
            if (chunkDataLen - offset < 8) break;

            Array.Copy(chunkData, (int)(chunkDataLen - offset - 4), endian, 0, 4);
            uint chunkLen = ToUInt32BigEndian(endian);

            Array.Copy(chunkData, (int)(chunkDataLen - offset - 8), endian, 0, 4);
            uint chunkId = ToUInt32BigEndian(endian);

            if (chunkLen == 0 || chunkLen > chunkDataLen - offset - 8)
                break;

            if (chunkId == ChunkResultPort)
            {
                int start = (int)(chunkDataLen - offset - 8 - chunkLen);
                rawJson = Encoding.UTF8.GetString(chunkData, start, (int)chunkLen);
                break;
            }

            offset += 8 + chunkLen;
        }

        if (string.IsNullOrEmpty(rawJson))
            return (rawJson, InspectionVerdict.Unknown, string.Empty, 0, 0);

        return ParseJson(rawJson);
    }

    private static (string rawJson, InspectionVerdict verdict, string solutionName, long total, long ng)
        ParseJson(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var solutionName = root.TryGetProperty("ScDeviceCurrentSolutionName", out var sn)
                ? sn.GetString() ?? string.Empty
                : string.Empty;

            var totalCount = root.TryGetProperty("ScDeviceSolutionTotalNumber", out var total)
                ? GetLong(total) : 0L;

            var ngCount = root.TryGetProperty("ScDeviceSolutionNgNumber", out var ng)
                ? GetLong(ng) : 0L;

            var verdict = InspectionVerdict.Unknown;
            if (root.TryGetProperty("ScDeviceSolutionRunningResult", out var result))
            {
                verdict = GetLong(result) switch
                {
                    0 => InspectionVerdict.Ok,
                    1 => InspectionVerdict.Ng,
                    _ => InspectionVerdict.Unknown,
                };
            }

            return (rawJson, verdict, solutionName, totalCount, ngCount);
        }
        catch
        {
            return (rawJson, InspectionVerdict.Unknown, string.Empty, 0, 0);
        }
    }

    /// <summary>
    /// Lee un valor numérico o string-numérico de un JsonElement.
    /// El firmware puede devolver los contadores como número o como string según versión.
    /// </summary>
    private static long GetLong(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetInt64(),
        JsonValueKind.String => long.TryParse(e.GetString(), out var n) ? n : 0L,
        _                    => 0L,
    };

    private static uint ToUInt32BigEndian(byte[] b) =>
        BitConverter.ToUInt32(new[] { b[3], b[2], b[1], b[0] }, 0);
}
