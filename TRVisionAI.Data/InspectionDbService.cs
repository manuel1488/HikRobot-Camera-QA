using TRVisionAI.Models;
using TRVisionAI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TRVisionAI.Data;

/// <summary>
/// Data access service for inspection sessions and frames.
/// Thread-safe: each operation opens and closes its own DbContext from the pool.
/// </summary>
public sealed class InspectionDbService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public InspectionDbService(IDbContextFactory<AppDbContext> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates a ready-to-use instance backed by the default database (%AppData%\TRVisionAI.Desktop\hikrobot.db).
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
    // Initialization
    // -------------------------------------------------------------------------

    /// <summary>Applies pending migrations and ensures the database exists.</summary>
    public async Task EnsureDatabaseAsync()
    {
        Directory.CreateDirectory(DbPathHelper.AppDataRoot);
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    // -------------------------------------------------------------------------
    // Sessions
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
    // Save frame
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

        // Update denormalized session counters
        var session = await db.Sessions.FindAsync(sessionId);
        if (session is not null)
        {
            if (frame.Verdict == InspectionVerdict.Ok) session.OkCount++;
            else if (frame.Verdict == InspectionVerdict.Ng) session.NgCount++;
        }

        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Queries
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

    /// <summary>Frames pending upload to the API (ApiSentAt == null).</summary>
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

    /// <summary>
    /// Loads the full frame detail: entity with modules + image bytes from disk.
    /// Returns null if the frame has not been persisted yet (possible race condition for very recent frames).
    /// </summary>
    public async Task<FrameDetail?> GetFrameDetailAsync(int sessionId, DateTime receivedAt)
    {
        await using var db = await _factory.CreateDbContextAsync();

        // Use ReceivedAt as the lookup key because the SDK's nImageNum can repeat across captures.
        var utc    = receivedAt.ToUniversalTime();
        var entity = await db.Frames
            .AsNoTracking()
            .Include(f => f.Modules)
            .FirstOrDefaultAsync(f => f.SessionId == sessionId && f.ReceivedAt == utc);

        if (entity is null) return null;

        byte[]? imageBytes = null;
        byte[]? maskBytes  = null;

        if (entity.ImagePath is not null)
        {
            var abs = DbPathHelper.ToAbsolutePath(entity.ImagePath);
            if (File.Exists(abs)) imageBytes = await File.ReadAllBytesAsync(abs);
        }

        if (entity.MaskImagePath is not null)
        {
            var abs = DbPathHelper.ToAbsolutePath(entity.MaskImagePath);
            if (File.Exists(abs)) maskBytes = await File.ReadAllBytesAsync(abs);
        }

        return new FrameDetail(entity, imageBytes, maskBytes);
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

        // Use the timestamp as the file name to guarantee uniqueness even when the SDK's nImageNum repeats.
        string fileName     = $"{date:HHmmss_fff}_{suffix}.jpg";
        string relativePath = DbPathHelper.BuildRelativePath(date, fileName);
        string absPath      = DbPathHelper.EnsureDirectory(relativePath);

        File.WriteAllBytes(absPath, bytes);
        return relativePath;
    }
}
