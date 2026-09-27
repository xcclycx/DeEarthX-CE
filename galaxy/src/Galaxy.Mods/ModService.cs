using Galaxy.Core;
using Galaxy.Data;
using Microsoft.EntityFrameworkCore;

namespace Galaxy.Mods;

public class ModService
{
    private readonly GalaxyDbContext _db;

    public ModService(GalaxyDbContext db) => _db = db;

    public async Task<GalaxyResult<Mod>> SubmitAsync(string modId, string type, int submittedBy)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return GalaxyResult<Mod>.Error(400, "modId 不能为空");
        if (type != "client" && type != "server")
            return GalaxyResult<Mod>.Error(400, "类型必须为 client 或 server");

        var mod = await _db.Mods.FirstOrDefaultAsync(m => m.ModId == modId);
        if (mod is null)
        {
            mod = new Mod
            {
                ModId = modId,
                ClientOk = type == "client",
                ServerOk = type == "server",
                SubmitCount = 1,
                SubmittedBy = submittedBy,
                Status = ModStatus.Pending
            };
            _db.Mods.Add(mod);
        }
        else
        {
            if (type == "client") mod.ClientOk = true;
            if (type == "server") mod.ServerOk = true;
            mod.SubmitCount++;
            // 如果之前被拒绝，重新提交时回到待审核状态
            if (mod.Status == ModStatus.Rejected)
            {
                mod.Status = ModStatus.Pending;
                mod.ReviewNote = null;
            }
            mod.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return GalaxyResult<Mod>.Ok(mod);
    }

    public async Task<GalaxyResult<Mod>> GetByModIdAsync(string modId, bool onlyApproved = false)
    {
        var q = _db.Mods.AsQueryable();
        if (onlyApproved) q = q.Where(m => m.Status == ModStatus.Approved);
        var mod = await q.FirstOrDefaultAsync(m => m.ModId == modId);
        if (mod is null) return GalaxyResult<Mod>.Error(404, "模组不存在");
        return GalaxyResult<Mod>.Ok(mod);
    }

    public async Task<GalaxyResult<PagedResult<Mod>>> SearchAsync(string? query, int page, int pageSize, ModStatus? status = null, bool onlyApproved = false)
    {
        query = query?.Trim();
        var q = _db.Mods.AsQueryable();
        if (!string.IsNullOrEmpty(query))
            q = q.Where(m => m.ModId.Contains(query));
        if (status.HasValue)
            q = q.Where(m => m.Status == status.Value);
        if (onlyApproved)
            q = q.Where(m => m.Status == ModStatus.Approved);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(m => m.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return GalaxyResult<PagedResult<Mod>>.Ok(new PagedResult<Mod> { Items = items, Total = total, Page = page, PageSize = pageSize });
    }

    public async Task<GalaxyResult<StatsResult>> GetStatsAsync()
    {
        // 单次查询获取所有统计数据，避免多次 CountAsync 的并发问题
        var stats = await _db.Mods.GroupBy(_ => 1).Select(g => new StatsResult
        {
            TotalMods = g.Count(),
            Pending = g.Count(m => m.Status == ModStatus.Pending),
            Approved = g.Count(m => m.Status == ModStatus.Approved),
            Rejected = g.Count(m => m.Status == ModStatus.Rejected),
            ClientOk = g.Count(m => m.ClientOk && m.Status == ModStatus.Approved),
            ServerOk = g.Count(m => m.ServerOk && m.Status == ModStatus.Approved),
            BothOk = g.Count(m => m.ClientOk && m.ServerOk && m.Status == ModStatus.Approved),
        }).FirstOrDefaultAsync();

        return GalaxyResult<StatsResult>.Ok(stats ?? new StatsResult());
    }

    public async Task<GalaxyResult<Mod>> ReviewAsync(int id, ModStatus status, string? reviewNote)
    {
        var mod = await _db.Mods.FindAsync(id);
        if (mod is null) return GalaxyResult<Mod>.Error(404, "模组不存在");

        mod.Status = status;
        mod.ReviewNote = reviewNote;
        mod.UpdatedAt = DateTime.UtcNow;

        // 审核拒绝时清除合规标记
        if (status == ModStatus.Rejected)
        {
            mod.ClientOk = false;
            mod.ServerOk = false;
        }

        await _db.SaveChangesAsync();
        return GalaxyResult<Mod>.Ok(mod);
    }

    public async Task<GalaxyResult<Mod>> UpdateModAsync(int id, bool? clientOk, bool? serverOk, string? note, string? reviewNote = null)
    {
        var mod = await _db.Mods.FindAsync(id);
        if (mod is null) return GalaxyResult<Mod>.Error(404, "模组不存在");

        if (clientOk.HasValue) mod.ClientOk = clientOk.Value;
        if (serverOk.HasValue) mod.ServerOk = serverOk.Value;
        if (note is not null) mod.Note = note;
        if (reviewNote is not null) mod.ReviewNote = reviewNote;
        mod.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return GalaxyResult<Mod>.Ok(mod);
    }

    public async Task<GalaxyResult> DeleteModAsync(int id)
    {
        var mod = await _db.Mods.FindAsync(id);
        if (mod is null) return GalaxyResult.Error(404, "模组不存在");
        _db.Mods.Remove(mod);
        await _db.SaveChangesAsync();
        return GalaxyResult.Ok("模组已删除");
    }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class StatsResult
{
    public int TotalMods { get; set; }
    public int Pending { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int ClientOk { get; set; }
    public int ServerOk { get; set; }
    public int BothOk { get; set; }
}
