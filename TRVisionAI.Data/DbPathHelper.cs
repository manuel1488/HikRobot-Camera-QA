namespace TRVisionAI.Data;

public static class DbPathHelper
{
    /// <summary>
    /// Carpeta raíz donde se guarda hikrobot.db y las imágenes.
    /// %AppData%\TRVisionAI.Desktop\
    /// </summary>
    public static string AppDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "TRVisionAI.Desktop");

    public static string DatabasePath { get; } =
        Path.Combine(AppDataRoot, "hikrobot.db");

    public static string ImagesRoot { get; } =
        Path.Combine(AppDataRoot, "images");

    /// <summary>
    /// Devuelve la ruta completa para guardar una imagen dado su path relativo.
    /// </summary>
    public static string ToAbsolutePath(string relativePath) =>
        Path.Combine(ImagesRoot, relativePath);

    /// <summary>
    /// Construye la ruta relativa organizada por fecha: YYYY\MM\DD\{fileName}
    /// </summary>
    public static string BuildRelativePath(DateTime date, string fileName)
    {
        string dir = Path.Combine(date.Year.ToString("D4"),
                                  date.Month.ToString("D2"),
                                  date.Day.ToString("D2"));
        return Path.Combine(dir, fileName);
    }

    /// <summary>
    /// Garantiza que el directorio absoluto del path relativo existe.
    /// </summary>
    public static string EnsureDirectory(string relativePath)
    {
        string abs = ToAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        return abs;
    }
}
