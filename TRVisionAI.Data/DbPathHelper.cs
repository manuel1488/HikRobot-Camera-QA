namespace TRVisionAI.Data;

public static class DbPathHelper
{
    /// <summary>
    /// Root folder where hikrobot.db and captured images are stored.
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
    /// Returns the absolute path for an image given its relative path.
    /// </summary>
    public static string ToAbsolutePath(string relativePath) =>
        Path.Combine(ImagesRoot, relativePath);

    /// <summary>
    /// Builds a date-organised relative path: YYYY\MM\DD\{fileName}
    /// </summary>
    public static string BuildRelativePath(DateTime date, string fileName)
    {
        string dir = Path.Combine(date.Year.ToString("D4"),
                                  date.Month.ToString("D2"),
                                  date.Day.ToString("D2"));
        return Path.Combine(dir, fileName);
    }

    /// <summary>
    /// Ensures the absolute directory for the given relative path exists.
    /// </summary>
    public static string EnsureDirectory(string relativePath)
    {
        string abs = ToAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        return abs;
    }
}
