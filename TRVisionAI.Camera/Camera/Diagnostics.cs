using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using MvVSControlSDKNet;

namespace TRVisionAI.Camera;

/// <summary>
/// Diagnósticos de conectividad y autenticación para la cámara Hikrobot.
/// Útil para resolver errores de login antes de iniciar la captura.
/// </summary>
public static class Diagnostics
{
    // Credenciales por defecto de fábrica — confirmadas en los demos oficiales del SDK:
    //   Demo/C/GrabImage/test.cpp línea 248-249: "Admin" / "Abc1234"
    //   Demo/C#/Winform/BasicDemo/Form1.cs: Login("Admin", password)
    private static readonly (string user, string pass)[] DefaultCredentials =
    [
        ("Admin",         "Abc1234"),   // ← credenciales de fábrica del SDK
        ("Admin",         ""),
        ("Admin",         "admin"),
        ("admin",         ""),
        ("admin",         "Abc1234"),
        ("admin",         "admin"),
        ("Administrator", ""),
        ("Administrator", "Abc1234")
    ];

    public static void Run(CameraInfo camera)
    {
        Console.WriteLine();
        PrintSection("DIAGNÓSTICO DE CÁMARA");

        PrintSdkVersion();
        PrintNetworkInterfaces(camera);
        PrintDeviceInfo(camera);
        TestHandle(camera);
        ProbeCredentials(camera);
    }

    // -------------------------------------------------------------------------

    private static void PrintSdkVersion()
    {
        PrintSection("1. Versión del SDK");
        // CSystem no expone GetSDKVersion en .NET — solo en C API
        // Lo indicamos como referencia informativa
        Console.WriteLine("  SDK:  MvVSControlSDK.Net  (ver. instalada con SCMVS)");
        Console.WriteLine("  DLL:  MvVisionSensorControl.dll  (win64)");
    }

    private static void PrintNetworkInterfaces(CameraInfo camera)
    {
        PrintSection("2. Interfaces de red del PC");

        // La cámara está en 169.254.x.x (LLA) → el PC debe tener una NIC en ese rango
        // para que la comunicación funcione correctamente
        bool foundCompatible = false;
        string cameraSubnet = string.Join(".", camera.IpAddress.Split('.').Take(2)); // "169.254"

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var ipProps = nic.GetIPProperties();
            foreach (var addr in ipProps.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;

                string ip = addr.Address.ToString();
                string subnet = string.Join(".", ip.Split('.').Take(2));
                bool compatible = subnet == cameraSubnet;

                if (compatible) foundCompatible = true;

                Console.Write($"  {nic.Name,-30} {ip,-18}");
                if (compatible)
                {
                    WriteColor(" ← compatible con la cámara", ConsoleColor.Green);
                }
                Console.WriteLine();
            }
        }

        if (!foundCompatible)
        {
            WriteColor($"\n  ADVERTENCIA: Ninguna NIC tiene IP en el rango {cameraSubnet}.x.x\n", ConsoleColor.Yellow);
            Console.WriteLine($"  La cámara usa IP LLA ({camera.IpAddress}). El PC debe tener");
            Console.WriteLine($"  una NIC configurada en el mismo segmento ({cameraSubnet}.x.x)");
            Console.WriteLine($"  o con IP asignada automáticamente (APIPA) en esa interfaz.");
        }
    }

    private static void PrintDeviceInfo(CameraInfo camera)
    {
        PrintSection("3. Información del dispositivo");

        var info = camera.SdkInfo;

        Console.WriteLine($"  Modelo        : {camera.ModelName}");
        Console.WriteLine($"  Número de serie: {camera.SerialNumber}");
        Console.WriteLine($"  Nombre usuario: {(string.IsNullOrWhiteSpace(camera.UserName) ? "(sin nombre)" : camera.UserName)}");
        Console.WriteLine($"  IP actual     : {camera.IpAddress}");

        // Modo de IP (nIpCfgCurrent es uint, comparar con uint)
        string ipMode = (info.nIpCfgCurrent >> 24) switch
        {
            0x05u => "Static",
            0x06u => "DHCP",
            0x04u => "LLA (Link-Local — sin DHCP/Static)",
            _     => $"Desconocido (0x{info.nIpCfgCurrent:X8})"
        };
        Console.Write($"  Modo IP       : ");
        if (ipMode.StartsWith("LLA"))
            WriteColor(ipMode + " ⚠ IP temporal, asignar IP estática para producción", ConsoleColor.Yellow);
        else
            Console.Write(ipMode);
        Console.WriteLine();

        // MAC
        uint macHi = info.nMacAddrHigh;
        uint macLo = info.nMacAddrLow;
        string mac = $"{(macHi >> 8) & 0xFF:X2}:{macHi & 0xFF:X2}:{(macLo >> 24) & 0xFF:X2}:{(macLo >> 16) & 0xFF:X2}:{(macLo >> 8) & 0xFF:X2}:{macLo & 0xFF:X2}";
        Console.WriteLine($"  MAC           : {mac}");

        // Versión
        string fw = info.chDeviceVersion.TrimEnd('\0');
        Console.WriteLine($"  Versión FW    : {(string.IsNullOrWhiteSpace(fw) ? "(no disponible)" : fw)}");
    }

    private static void TestHandle(CameraInfo camera)
    {
        PrintSection("4. Test de conectividad (sin login)");

        var device = new CDevice();
        var sdkInfo = camera.SdkInfo;

        int ret = device.CreateHandle(ref sdkInfo);
        if (ret != CErrorCode.MV_VS_OK)
        {
            WriteColor($"  FALLO CreateHandle — 0x{ret:X8}", ConsoleColor.Red);
            Console.WriteLine("  → Problema de red o driver GigE Vision no instalado.");
            device.DestroyHandle();
            return;
        }

        WriteColor("  CreateHandle  OK", ConsoleColor.Green);

        // Ping básico
        Console.Write($"  Ping {camera.IpAddress,-18}");
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(camera.IpAddress, 1000);
            if (reply.Status == IPStatus.Success)
                WriteColor($" OK ({reply.RoundtripTime} ms)", ConsoleColor.Green);
            else
                WriteColor($" Sin respuesta ({reply.Status})", ConsoleColor.Yellow);
        }
        catch (Exception ex)
        {
            WriteColor($" Error: {ex.Message}", ConsoleColor.Yellow);
        }
        Console.WriteLine();

        device.DestroyHandle();
    }

    private static void ProbeCredentials(CameraInfo camera)
    {
        PrintSection("5. Prueba de credenciales");
        Console.WriteLine("  Probando credenciales comunes con tres variantes de Login:\n");
        Console.WriteLine($"  {"Usuario",-16} {"Contraseña",-12} {"Login(plain)",-16} {"LoginEX(false)",-18} {"LoginEX(MD5)"}");
        Console.WriteLine($"  {new string('─', 78)}");

        bool found = false;

        foreach (var (user, pass) in DefaultCredentials)
        {
            string passDisplay = string.IsNullOrEmpty(pass) ? "(vacía)" : new string('*', pass.Length);
            Console.Write($"  {user,-16} {passDisplay,-12} ");

            // --- Variante A: Login() texto plano ---
            string resultA = TryLogin(camera, user, pass, loginEx: false, encrypt: false, out bool sessionActive);
            WriteLoginResult(resultA);
            Console.Write($"  {"",-2}");

            // --- Variante B: LoginEX(bEncryption=false) ---
            string resultB = TryLogin(camera, user, pass, loginEx: true, encrypt: false, out sessionActive);
            WriteLoginResult(resultB);
            Console.Write($"  {"",-2}");

            // --- Variante C: LoginEX(bEncryption=true) con MD5 ---
            string md5Pass = PasswordHelper.ToMd5Hex(pass);
            string resultC = TryLogin(camera, user, md5Pass, loginEx: true, encrypt: true, out sessionActive);
            WriteLoginResult(resultC);
            Console.WriteLine();

            bool anyOk = resultA == "OK" || resultB == "OK" || resultC == "OK";
            if (anyOk)
            {
                string method = resultA == "OK" ? "Login(plain)"
                              : resultB == "OK" ? "LoginEX(encrypt=false)"
                                                : $"LoginEX(encrypt=true, MD5(\"{pass}\"))";
                Console.WriteLine();
                WriteColor($"  ✓ Credenciales válidas encontradas!", ConsoleColor.Green);
                Console.WriteLine($"\n    usuario   = \"{user}\"");
                Console.WriteLine($"    contraseña = \"{pass}\"");
                Console.WriteLine($"    método     = {method}");
                found = true;
                break;
            }

            if (sessionActive)
            {
                Console.WriteLine();
                WriteColor("  DIAGNÓSTICO: La cámara ya tiene una conexión activa.", ConsoleColor.Yellow);
                Console.WriteLine("  → Cerrar SCMVS y cualquier otra aplicación que use la cámara.");
                Console.WriteLine("  → Esperar ~10 segundos y volver a intentar.");
                break;
            }
        }

        if (!found)
        {
            Console.WriteLine();
            WriteColor("\n  No se encontraron credenciales válidas entre las predeterminadas.", ConsoleColor.Yellow);
            Console.WriteLine("  → Verificar en SCMVS: Settings → User Management");
            Console.WriteLine("  → El usuario por defecto de Hikrobot es 'admin' (todo minúsculas)");
            Console.WriteLine("  → Si la cuenta fue bloqueada, reiniciar la cámara para desbloquearla");
        }
    }

    /// <summary>Intenta hacer login y retorna "OK" o el hint del error.</summary>
    private static string TryLogin(CameraInfo camera, string user, string pass,
        bool loginEx, bool encrypt, out bool sessionActive)
    {
        sessionActive = false;
        var device = new CDevice();
        var sdkInfo = camera.SdkInfo;

        if (device.CreateHandle(ref sdkInfo) != CErrorCode.MV_VS_OK)
            return "handle?";

        int ret = loginEx
            ? device.LoginEX(user, pass, encrypt)
            : device.Login(user, pass);

        if (ret == CErrorCode.MV_VS_OK)
        {
            device.Logout();
            device.DestroyHandle();
            return "OK";
        }

        device.DestroyHandle();

        if ((uint)ret == 0x80030506u) sessionActive = true;

        return (uint)ret switch
        {
            0x80030500u => "pass err",
            0x80030501u => "pass vacía",
            0x80030506u => "sesión activa",
            0x80030507u => "login fail",
            0x80030203u => "denegado",
            _           => $"0x{(uint)ret:X8}"
        };
    }

    private static void WriteLoginResult(string result)
    {
        if (result == "OK")
            WriteColor($"{"OK",-16}", ConsoleColor.Green);
        else if (result.Contains("activa"))
            WriteColor($"{result,-16}", ConsoleColor.Yellow);
        else
            WriteColor($"{result,-16}", ConsoleColor.DarkRed);
    }

    // -------------------------------------------------------------------------

    private static void PrintSection(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"  ┌─ {title} ");
        Console.ResetColor();
    }

    private static void WriteColor(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ResetColor();
    }
}
