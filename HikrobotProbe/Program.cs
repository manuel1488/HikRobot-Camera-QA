using Hikrobot.Camera;
using Hikrobot.Models;

// ---------------------------------------------------------------------------
// Hikrobot Probe — herramienta de validación de comunicación con MV-SC3050M
// ---------------------------------------------------------------------------

Console.OutputEncoding = System.Text.Encoding.UTF8;

PrintBanner();

// Directorio de salida para imágenes y log JSON
string outputDir = Path.Combine(AppContext.BaseDirectory, "output");
Directory.CreateDirectory(outputDir);
string jsonLogPath = Path.Combine(outputDir, $"results_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl");

// ---------------------------------------------------------------------------
// 1. Enumerar cámaras
// ---------------------------------------------------------------------------

Print("Buscando cámaras en la red...", ConsoleColor.Cyan);

List<CameraInfo> cameras;
try
{
    cameras = CameraClient.EnumerateDevices();
}
catch (HikrobotException ex)
{
    PrintError($"Error al enumerar dispositivos: {ex.Message}");
    return 1;
}

if (cameras.Count == 0)
{
    PrintError("No se encontraron cámaras. Verificar conexión de red y que SCMVS esté instalado.");
    return 1;
}

Print($"Se encontraron {cameras.Count} cámara(s):\n", ConsoleColor.Green);
foreach (var cam in cameras)
    Console.WriteLine($"  {cam}");

// ---------------------------------------------------------------------------
// 2. Seleccionar cámara
// ---------------------------------------------------------------------------

CameraInfo selected;
if (cameras.Count == 1)
{
    selected = cameras[0];
    Print($"\nUsando la única cámara disponible: {selected.IpAddress}", ConsoleColor.Cyan);
}
else
{
    Console.Write("\nSeleccionar índice de cámara: ");
    if (!int.TryParse(Console.ReadLine(), out int idx) || idx < 0 || idx >= cameras.Count)
    {
        PrintError("Índice inválido.");
        return 1;
    }
    selected = cameras[idx];
}

// ---------------------------------------------------------------------------
// 2b. Menú de acción
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("  [C] Conectar y capturar");
Console.WriteLine("  [D] Diagnósticos (debug de conexión)");
Console.Write("\nOpción: ");
string action = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();

if (action == "D")
{
    Diagnostics.Run(selected);
    Console.WriteLine("\nPresiona Enter para salir.");
    Console.ReadLine();
    return 0;
}

if (action != "C" && action != "")
{
    PrintError("Opción inválida.");
    return 1;
}

// ---------------------------------------------------------------------------
// 3. Credenciales
// ---------------------------------------------------------------------------

Console.Write("\nUsuario (Enter = Admin): ");
string user = Console.ReadLine()?.Trim() is { Length: > 0 } u ? u : "Admin";

Console.Write("Contraseña (Enter = vacía): ");
string password = ReadPassword();

Console.WriteLine("  [P] Texto plano  (default)");
Console.WriteLine("  [M] MD5 hash     (si la cámara requiere contraseña cifrada)");
Console.Write("Modo de contraseña (Enter = P): ");
string passMode = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();

bool useEncryption = passMode == "M";
string loginPassword = useEncryption
    ? PasswordHelper.ToMd5Hex(password)
    : password;

// ---------------------------------------------------------------------------
// 4. Conectar
// ---------------------------------------------------------------------------

using var client = new CameraClient();

string modeLabel = useEncryption ? "MD5 hash" : "texto plano";
Print($"\nConectando a {selected.IpAddress} como '{user}' (contraseña: {modeLabel})...", ConsoleColor.Cyan);
try
{
    client.Connect(selected, user, loginPassword, encryptPassword: useEncryption);
}
catch (HikrobotException ex)
{
    PrintError($"No se pudo conectar: {ex.Message}");
    return 1;
}
Print("Conectado.", ConsoleColor.Green);

// ---------------------------------------------------------------------------
// 5. Iniciar adquisición
// ---------------------------------------------------------------------------

try
{
    client.StartAcquisition();
}
catch (HikrobotException ex)
{
    PrintError($"No se pudo iniciar la adquisición: {ex.Message}");
    return 1;
}

Print("Adquisición iniciada en modo continuo. Presiona Ctrl+C o Q para detener, V para volcar el siguiente frame.\n", ConsoleColor.Cyan);
PrintTableHeader();

// ---------------------------------------------------------------------------
// 6. Loop de captura
// ---------------------------------------------------------------------------

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

bool dumpNextFrame = false;

// Hilo de teclado no bloqueante (para detectar 'Q' y 'V')
_ = Task.Run(() =>
{
    while (!cts.IsCancellationRequested)
    {
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
            {
                cts.Cancel();
                break;
            }
            if (key.Key == ConsoleKey.V)
                dumpNextFrame = true;
        }
        Thread.Sleep(100);
    }
});

int okCount = 0, ngCount = 0, errorCount = 0;

while (!cts.IsCancellationRequested)
{
    InspectionFrame? frame;
    try
    {
        frame = client.TryGetFrame(timeoutMs: 1000);
    }
    catch (HikrobotException ex)
    {
        errorCount++;
        PrintError($"Error en GetResultData: {ex.Message}");
        if (errorCount >= 5)
        {
            PrintError("Demasiados errores consecutivos. Deteniendo.");
            break;
        }
        continue;
    }

    if (frame is null) continue; // timeout — la cámara no ha disparado aún
    errorCount = 0;

    if (dumpNextFrame)
    {
        dumpNextFrame = false;
        PrintFrameDump(frame);
    }

    // Contadores
    if      (frame.Verdict == InspectionVerdict.Ok) okCount++;
    else if (frame.Verdict == InspectionVerdict.Ng) ngCount++;

    // Imprimir en consola
    PrintFrameRow(frame, okCount + ngCount);

    // Guardar imagen
    if (frame.ImageBytes is { Length: > 0 })
    {
        string verdictStr = frame.Verdict switch
        {
            InspectionVerdict.Ok => "OK",
            InspectionVerdict.Ng => "NG",
            _                    => "UNKNOWN",
        };
        string imgPath = Path.Combine(outputDir,
            $"{frame.ReceivedAt:yyyyMMdd_HHmmss_fff}_{frame.FrameNumber}_{verdictStr}.jpg");
        await File.WriteAllBytesAsync(imgPath, frame.ImageBytes, cts.Token).ConfigureAwait(false);
    }

    // Guardar JSON raw (newline-delimited JSON — fácil de procesar luego)
    if (!string.IsNullOrEmpty(frame.RawJson))
    {
        string logLine = $"{{\"ts\":\"{frame.ReceivedAt:O}\",\"frame\":{frame.FrameNumber},"
                       + $"\"verdict\":\"{frame.Verdict}\","
                       + $"\"raw\":{frame.RawJson}}}\n";
        await File.AppendAllTextAsync(jsonLogPath, logLine, cts.Token).ConfigureAwait(false);
    }
}

// ---------------------------------------------------------------------------
// 7. Resumen y cierre
// ---------------------------------------------------------------------------

client.StopAcquisition();

Console.WriteLine();
Print("=== Resumen de la sesión ===", ConsoleColor.Cyan);
Console.WriteLine($"  OK      : {okCount}");
Console.WriteLine($"  NG      : {ngCount}");
Console.WriteLine($"  Total   : {okCount + ngCount}");
Console.WriteLine($"  Imágenes: {outputDir}");
Console.WriteLine($"  Log JSON: {jsonLogPath}");

return 0;

// ---------------------------------------------------------------------------
// Helpers de presentación
// ---------------------------------------------------------------------------

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine("╔═══════════════════════════════════════════╗");
    Console.WriteLine("║   Hikrobot Probe — MV-SC3050M-08M-WBN    ║");
    Console.WriteLine("╚═══════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine();
}

static void PrintTableHeader()
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"{"#",-6} {"Hora",-14} {"Frame",-10} {"Resultado",-10} {"Solución",-24} {"Total",7} {"NG",7}");
    Console.WriteLine(new string('─', 80));
    Console.ResetColor();
}

static void PrintFrameRow(InspectionFrame f, int rowNum)
{
    Console.Write($"{rowNum,-6} {f.ReceivedAt:HH:mm:ss.fff}  {f.FrameNumber,-10} ");

    switch (f.Verdict)
    {
        case InspectionVerdict.Ok:
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{"OK",-10}");
            break;
        case InspectionVerdict.Ng:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"{"NG",-10}");
            break;
        default:
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"{"?",-10}");
            break;
    }

    Console.ResetColor();
    Console.WriteLine($" {f.SolutionName,-24} {f.TotalCount,7} {f.NgCount,7}");
}

static void Print(string msg, ConsoleColor color)
{
    Console.ForegroundColor = color;
    Console.WriteLine(msg);
    Console.ResetColor();
}

static void PrintError(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"[ERROR] {msg}");
    Console.ResetColor();
}

static void PrintFrameDump(InspectionFrame f)
{
    var d = f.Debug;
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine("┌─── FRAME DUMP ─────────────────────────────────────────────");
    Console.ResetColor();
    Console.WriteLine($"│ FrameNumber  : {f.FrameNumber}");
    Console.WriteLine($"│ ImageLen     : {d.ImageLen} bytes");
    Console.WriteLine($"│ ImageWidth   : {f.ImageWidth}  Height: {f.ImageHeight}");
    Console.WriteLine($"│ HasImagePtr  : {d.HasImagePtr}");
    Console.WriteLine($"│ ChunkDataLen : {d.ChunkDataLen} bytes");
    Console.WriteLine($"│ HasChunkPtr  : {d.HasChunkPtr}");

    if (d.ChunkDataLen == 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("│");
        Console.WriteLine("│ ADVERTENCIA: ChunkDataLen = 0");
        Console.WriteLine("│ → La cámara no envía datos de resultado.");
        Console.WriteLine("│ → Verificar que haya una solución cargada y CORRIENDO en SCMVS.");
        Console.ResetColor();
    }
    else
    {
        Console.WriteLine($"│");
        Console.WriteLine($"│ ChunkData HEAD (hex): {d.ChunkHexDump}");
        Console.WriteLine($"│ ChunkData TAIL (hex): {d.ChunkHexTail}");
        Console.WriteLine($"│");
        Console.WriteLine($"│ ChunkData como texto:");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"│   {d.ChunkAsText.Replace("\n", "\n│   ")}");
        Console.ResetColor();
    }

    if (!string.IsNullOrEmpty(f.RawJson))
    {
        Console.WriteLine($"│");
        Console.WriteLine($"│ JSON parseado:");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"│   {f.RawJson}");
        Console.ResetColor();
    }

    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine("└────────────────────────────────────────────────────────────");
    Console.ResetColor();
    Console.WriteLine();
}

static string ReadPassword()
{
    var sb = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
        if (key.Key == ConsoleKey.Backspace && sb.Length > 0) { sb.Remove(sb.Length - 1, 1); Console.Write("\b \b"); }
        else if (key.Key != ConsoleKey.Backspace) { sb.Append(key.KeyChar); Console.Write('*'); }
    }
    return sb.ToString();
}
