using System.Text;
using System.Text.Json;
using DeEarthX.Core.Abstractions;
using DeEarthX.Infrastructure.Http;
using DeEarthX.Infrastructure.Toml;
using DeEarthX.Infrastructure.Zip;

namespace DeEarthX.Galaxy;

public sealed record ModIdResult(string FileName, string? ModId, string? Error);

public sealed class GalaxyService
{
    private const string DefaultApiBase = "https://galaxy.xcclyc.com.cn";
    private const string EnvApiBase = "GALAXY_API_BASE";
    private const string EnvApiKey = "GALAXY_API_KEY";

    private const string ForgeDescriptor = "META-INF/mods.toml";
    private const string NeoForgeDescriptor = "META-INF/neoforge.mods.toml";
    private const string FabricDescriptor = "fabric.mod.json";

    private static readonly string[] ModDescriptors =
    {
        ForgeDescriptor, NeoForgeDescriptor, FabricDescriptor
    };

    private readonly IZipService _zipService;
    private readonly ITomlService _tomlService;
    private readonly IDeEarthXHttpService _httpService;
    private readonly ILogService _logService;

    public string ApiBase { get; }
    public string? ApiKey { get; }

    public GalaxyService(
        IZipService zipService,
        ITomlService tomlService,
        IDeEarthXHttpService httpService,
        ILogService logService,
        string? configApiBase = null,
        string? configApiKey = null)
    {
        _zipService = zipService;
        _tomlService = tomlService;
        _httpService = httpService;
        _logService = logService;

        // 优先级：环境变量 > appsettings.json 配置 > 默认值
        ApiBase = Environment.GetEnvironmentVariable(EnvApiBase) ?? configApiBase ?? DefaultApiBase;
        ApiKey = Environment.GetEnvironmentVariable(EnvApiKey) ?? configApiKey;

        if (!string.IsNullOrEmpty(ApiKey))
        {
            _logService.Info($"Galaxy API 已配置: {ApiBase}（使用 API Key 认证）");
        }
        else
        {
            _logService.Info($"Galaxy API 已配置: {ApiBase}（无 API Key）");
        }
    }

    public async Task<List<ModIdResult>> ParseModIdsAsync(
        IEnumerable<(string FileName, byte[] Content)> jars,
        CancellationToken ct)
    {
        var results = new List<ModIdResult>();
        foreach (var (fileName, content) in jars)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var modId = await TryParseModIdAsync(content, ct).ConfigureAwait(false);
                results.Add(modId is null
                    ? new ModIdResult(fileName, null, "未找到 modId")
                    : new ModIdResult(fileName, modId, null));
            }
            catch (Exception ex)
            {
                results.Add(new ModIdResult(fileName, null, ex.Message));
            }
        }

        return results;
    }

    public async Task<object?> SubmitAsync(string type, string modid, CancellationToken ct)
    {
        if (type != "server" && type != "client")
        {
            throw new ArgumentException("无效的类型参数", nameof(type));
        }

        if (string.IsNullOrEmpty(modid))
        {
            throw new ArgumentException("未提供 modid", nameof(modid));
        }

        var url = $"{ApiBase}/api/mod/submit/{type}";
        _logService.Info($"正在提交 {type} 端模组 ID 到 {url}", new { modid });
        try
        {
            object? result;
            if (!string.IsNullOrEmpty(ApiKey))
            {
                result = await _httpService
                    .PostJsonWithAuthAsync<object?>(url, new { modid }, "Bearer", ApiKey!, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                result = await _httpService
                    .PostJsonAsync<object?>(url, new { modid }, ct)
                    .ConfigureAwait(false);
            }

            _logService.Info($"已成功提交 {type} 端模组 ID", new { modid, result });
            return result;
        }
        catch (Exception ex)
        {
            _logService.Error($"提交 {type} 端模组 ID 失败", new { modid, error = ex.Message });
            throw;
        }
    }

    private async Task<string?> TryParseModIdAsync(byte[] content, CancellationToken ct)
    {
        var entries = await _zipService
            .ReadEntriesAsync(content, ModDescriptors)
            .ConfigureAwait(false);

        if (entries.TryGetValue(ForgeDescriptor, out var forgeBytes) && forgeBytes.Length > 0)
        {
            return ExtractTomlModId(forgeBytes);
        }

        if (entries.TryGetValue(NeoForgeDescriptor, out var neoBytes) && neoBytes.Length > 0)
        {
            return ExtractTomlModId(neoBytes);
        }

        if (entries.TryGetValue(FabricDescriptor, out var fabricBytes) && fabricBytes.Length > 0)
        {
            return ExtractFabricModId(fabricBytes);
        }

        return null;
    }

    private string? ExtractTomlModId(byte[] bytes)
    {
        var content = Encoding.UTF8.GetString(bytes);
        var table = _tomlService.Parse(content);
        var id = _tomlService.GetModId(table);
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private string? ExtractFabricModId(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("id", out var idElement))
        {
            var id = idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : idElement.ToString();
            return string.IsNullOrEmpty(id) ? null : id;
        }

        return null;
    }
}
