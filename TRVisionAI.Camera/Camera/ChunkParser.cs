using System.Text;
using System.Text.Json;
using TRVisionAI.Models;

namespace TRVisionAI.Camera;

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

    public static (
        string                     rawJson,
        InspectionVerdict          verdict,
        string                     solutionName,
        long                       total,
        long                       ng,
        IReadOnlyList<ModuleResult> modules,
        byte[]?                    maskImageBytes)
        ParseResult(byte[] chunkData, uint chunkDataLen)
    {
        uint   offset        = 0;
        var    endian        = new byte[4];
        string rawJson       = string.Empty;
        byte[]? maskBytes    = null;

        while (chunkDataLen > offset)
        {
            if (chunkDataLen - offset < 8) break;

            Array.Copy(chunkData, (int)(chunkDataLen - offset - 4), endian, 0, 4);
            uint chunkLen = ToUInt32BigEndian(endian);

            Array.Copy(chunkData, (int)(chunkDataLen - offset - 8), endian, 0, 4);
            uint chunkId = ToUInt32BigEndian(endian);

            if (chunkLen == 0 || chunkLen > chunkDataLen - offset - 8)
                break;

            int dataStart = (int)(chunkDataLen - offset - 8 - chunkLen);

            if (chunkId == ChunkResultPort && string.IsNullOrEmpty(rawJson))
            {
                rawJson = Encoding.UTF8.GetString(chunkData, dataStart, (int)chunkLen);
            }
            else if (chunkId == ChunkMaskImagePort && maskBytes is null && chunkLen > 16)
            {
                // Los primeros 16 bytes son header: ModuleID(4) + Format(4) + Width(4) + Height(4)
                // El resto son los bytes de imagen reales (JPEG cuando Format == 1)
                int imageLen = (int)chunkLen - 16;
                maskBytes = new byte[imageLen];
                Array.Copy(chunkData, dataStart + 16, maskBytes, 0, imageLen);
            }

            offset += 8 + chunkLen;
        }

        if (string.IsNullOrEmpty(rawJson))
            return (rawJson, InspectionVerdict.Unknown, string.Empty, 0, 0, [], maskBytes);

        var (verdict, solutionName, total, ng, modules) = ParseJson(rawJson);
        return (rawJson, verdict, solutionName, total, ng, modules, maskBytes);
    }

    private static (
        InspectionVerdict          verdict,
        string                     solutionName,
        long                       total,
        long                       ng,
        IReadOnlyList<ModuleResult> modules)
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

            // Fuente primaria: logic.solution_sts o logic.param_status_string_solution
            // (string explícito "OK"/"NG" del módulo lógico que es el árbitro final).
            // Fallback: ScDeviceSolutionRunningResult numérico (semántica ambigua en algunos firmware).
            var verdict = TryReadLogicSolutionVerdict(root)
                       ?? ReadNumericVerdict(root)
                       ?? InspectionVerdict.Unknown;

            var modules = ParseModules(root);
            return (verdict, solutionName, totalCount, ngCount, modules);
        }
        catch
        {
            return (InspectionVerdict.Unknown, string.Empty, 0, 0, []);
        }
    }

    private static IReadOnlyList<ModuleResult> ParseModules(JsonElement root)
    {
        JsonElement arr = default;
        bool found = root.TryGetProperty("CurrentData",   out arr) ||
                     root.TryGetProperty("ModuleResults", out arr) ||
                     root.TryGetProperty("moduleResults", out arr);

        if (!found || arr.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<ModuleResult>();
        foreach (var item in arr.EnumerateArray())
        {
            string name = string.Empty;
            if (item.TryGetProperty("ModuleName",    out var mn) ||
                item.TryGetProperty("moduleName",    out mn)     ||
                item.TryGetProperty("strModuleName", out mn))
                name = mn.GetString() ?? string.Empty;

            list.Add(new ModuleResult
            {
                ModuleName = name,
                Verdict    = ParseModuleVerdict(item),
                RawJson    = item.GetRawText(),
            });
        }

        return list;
    }

    /// <summary>
    /// Lee el veredicto de la solución desde el módulo "logic" en CurrentData.
    /// Busca los campos solution_sts o param_status_string_solution que contienen "OK"/"NG" explícito.
    /// Retorna null si no se encuentra el módulo o los campos.
    /// </summary>
    private static InspectionVerdict? TryReadLogicSolutionVerdict(JsonElement root)
    {
        if (!root.TryGetProperty("CurrentData", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var module in arr.EnumerateArray())
        {
            if (!module.TryGetProperty("ModuleName", out var mn) ||
                mn.GetString() != "logic") continue;

            if (!module.TryGetProperty("pInfo", out var pInfo) ||
                pInfo.ValueKind != JsonValueKind.Array) continue;

            foreach (var info in pInfo.EnumerateArray())
            {
                if (!info.TryGetProperty("strEnName", out var enNameEl)) continue;
                var enName = enNameEl.GetString();

                // solution_sts y param_status_string_solution son los campos más fiables
                if (enName is "solution_sts" or "param_status_string_solution")
                {
                    if (info.TryGetProperty("pStringValue", out var sv) &&
                        sv.ValueKind == JsonValueKind.Array)
                    {
                        var first = sv.EnumerateArray().FirstOrDefault();
                        if (first.ValueKind == JsonValueKind.Object &&
                            first.TryGetProperty("strValue", out var sval))
                        {
                            return sval.GetString() switch
                            {
                                "OK" => InspectionVerdict.Ok,
                                "NG" => InspectionVerdict.Ng,
                                _    => null,
                            };
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Fallback: lee ScDeviceSolutionRunningResult como numérico (0=Ok, 1=Ng).
    /// Puede tener semántica ambigua según versión de firmware — usar solo si no hay string explícito.
    /// </summary>
    private static InspectionVerdict? ReadNumericVerdict(JsonElement root)
    {
        if (!root.TryGetProperty("ScDeviceSolutionRunningResult", out var result))
            return null;

        return GetLong(result) switch
        {
            0 => InspectionVerdict.Ok,
            1 => InspectionVerdict.Ng,
            _ => null,
        };
    }

    /// <summary>
    /// Lee el veredicto de un módulo desde su array pInfo.
    /// Fuente primaria: pInfo[strEnName=="param_status_string"].pStringValue[0].strValue ("OK"/"NG").
    /// Fallback: pInfo[strEnName=="param_status"].pIntValue[0] donde 1=OK.
    /// </summary>
    private static InspectionVerdict ParseModuleVerdict(JsonElement item)
    {
        if (!item.TryGetProperty("pInfo", out var pInfo) || pInfo.ValueKind != JsonValueKind.Array)
            return InspectionVerdict.Unknown;

        string? statusString = null;
        int?    statusInt    = null;

        foreach (var info in pInfo.EnumerateArray())
        {
            if (!info.TryGetProperty("strEnName", out var enNameEl)) continue;
            var enName = enNameEl.GetString();

            if (enName == "param_status_string" && statusString is null)
            {
                if (info.TryGetProperty("pStringValue", out var sv) && sv.ValueKind == JsonValueKind.Array)
                {
                    var first = sv.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object &&
                        first.TryGetProperty("strValue", out var sval))
                        statusString = sval.GetString();
                }
            }
            else if (enName == "param_status" && statusInt is null)
            {
                if (info.TryGetProperty("pIntValue", out var iv) && iv.ValueKind == JsonValueKind.Array)
                {
                    var first = iv.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Number)
                        statusInt = first.GetInt32();
                }
            }

            if (statusString is not null && statusInt is not null) break;
        }

        // El string es la fuente autoritativa cuando está disponible
        if (statusString is "NG") return InspectionVerdict.Ng;
        if (statusString is "OK") return InspectionVerdict.Ok;

        // Fallback por int: 1 = OK de forma consistente en módulos sin string
        return statusInt == 1 ? InspectionVerdict.Ok : InspectionVerdict.Unknown;
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
