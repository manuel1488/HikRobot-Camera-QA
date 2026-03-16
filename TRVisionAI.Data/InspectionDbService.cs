using TRVisionAI.Models;
using TRVisionAI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TRVisionAI.Data;

/// <summary>
/// Servicio de acceso a datos para sesiones e inspecciones.
/// Thread-safe: cada operación abre y cierra su propio DbContext del pool.
/// </summary>
public sealed class InspectionDbService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public InspectionDbService(IDbContextFactory<AppDbContext> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Crea una instancia lista para usar con la base de datos por defecto (%AppData%\TRVisionAI.Desktop\hikrobot.db).
    /// </summary>
    public static InspectionDbService Create()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={DbPathHelper.DatabasePath}")
            .Options;

        return new InspectionDbService(new SimpleDbContextFactory(opts));
    }

    private sealed class SimpleDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _opts;
        public SimpleDbContextFactory(DbContextOptions<AppDbContext> opts) => _opts = opts;
        public AppDbContext CreateDbContext() => new(_opts);
    }

    // -------------------------------------------------------------------------
    // Inicialización
    // -------------------------------------------------------------------------

    /// <summary>Aplica migraciones pendientes y garantiza que la BD existe.</summary>
    public async Task EnsureDatabaseAsync()
    {
        Directory.CreateDirectory(DbPathHelper.AppDataRoot);
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    // -------------------------------------------------------------------------
    // Sesiones
    // -------------------------------------------------------------------------

    public async Task<int> BeginSessionAsync(string cameraIp, string cameraModel, string @operator)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var session = new SessionEntity
        {
            StartedAt   = DateTime.UtcNow,
            CameraIp    = cameraIp,
            CameraModel = cameraModel,
            Operator    = @operator,
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    public async Task EndSessionAsync(int sessionId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var session = await db.Sessions.FindAsync(sessionId);
        if (session is null) return;
        session.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Guardar frame
    // -------------------------------------------------------------------------

    public async Task SaveFrameAsync(int sessionId, InspectionFrame frame)
    {
        string? imagePath     = SaveImageFile(frame.ImageBytes,     frame.ReceivedAt, frame.FrameNumber, "img");
        string? maskImagePath = SaveImageFile(frame.MaskImageBytes, frame.ReceivedAt, frame.FrameNumber, "mask");

        await using var db = await _factory.CreateDbContextAsync();

        var entity = new FrameEntity
        {
            SessionId    = sessionId,
            FrameNumber  = (long)frame.FrameNumber,
            ReceivedAt   = frame.ReceivedAt.ToUniversalTime(),
            Verdict      = (int)frame.Verdict,
            SolutionName = frame.SolutionName,
            TotalCount   = frame.TotalCount,
            NgCount      = frame.NgCount,
            RawJson      = frame.RawJson,
            ImagePath    = imagePath,
            MaskImagePath = maskImagePath,
        };

        foreach (var m in frame.ModuleResults)
        {
            entity.Modules.Add(new ModuleEntity
            {
                ModuleName = m.ModuleName,
                Verdict    = (int)m.Verdict,
                RawJson    = m.RawJson,
            });
        }

        db.Frames.Add(entity);

        // Actualizar contadores denormalizados de la sesión
        var session = await db.Sessions.FindAsync(sessionId);
        if (session is not null)
        {
            if (frame.Verdict == InspectionVerdict.Ok) session.OkCount++;
            else if (frame.Verdict == InspectionVerdict.Ng) session.NgCount++;
        }

        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Consultas
    // -------------------------------------------------------------------------

    public async Task<List<FrameEntity>> GetFramesAsync(
        int sessionId, int skip = 0, int take = 200)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Frames
            .AsNoTracking()
            .Where(f => f.SessionId == sessionId)
            .OrderByDescending(f => f.ReceivedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
    }

    public async Task<List<FrameEntity>> GetNgFramesAsync(
        int sessionId, int skip = 0, int take = 200)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Frames
            .AsNoTracking()
            .Include(f => f.Modules)
            .Where(f => f.SessionId == sessionId && f.Verdict == (int)InspectionVerdict.Ng)
            .OrderByDescending(f => f.ReceivedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
    }

    /// <summary>Frames pendientes de enviar a la API (ApiSentAt == null).</summary>
    public async Task<List<FrameEntity>> GetPendingApiFramesAsync(int batchSize = 50)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Frames
            .AsNoTracking()
            .Include(f => f.Modules)
            .Where(f => f.ApiSentAt == null)
            .OrderBy(f => f.ReceivedAt)
            .Take(batchSize)
            .ToListAsync();
    }

    public async Task MarkApiSentAsync(IEnumerable<long> frameIds)
    {
        var ids   = frameIds.ToList();
        var sentAt = DateTime.UtcNow;

        await using var db = await _factory.CreateDbContextAsync();
        await db.Frames
            .Where(f => ids.Contains(f.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ApiSentAt, sentAt));
    }

    public async Task<List<SessionEntity>> GetSessionsAsync(int skip = 0, int take = 50)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Sessions
            .AsNoTracking()
            .OrderByDescending(s => s.StartedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string? SaveImageFile(byte[]? bytes, DateTime date, ulong frameNum, string suffix)
    {
        if (bytes is not { Length: > 0 }) return null;

        string fileName    = $"{frameNum:D10}_{suffix}.jpg";
        string relativePath = DbPathHelper.BuildRelativePath(date, fileName);
        string absPath      = DbPathHelper.EnsureDirectory(relativePath);

        File.WriteAllBytes(absPath, bytes);
        return relativePath;
    }
}
