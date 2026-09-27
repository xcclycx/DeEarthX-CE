using System.Text.Json;
using DeEarthX.Core.Abstractions;

namespace DeEarthX.Plugins;

public interface IPluginTemplateGenerator
{
    /// <summary>在插件目录中生成可运行的脚手架。</summary>
    Task GenerateAsync(string pluginDir, PluginManifest manifest, CancellationToken ct = default);
}

/// <summary>
/// 插件脚手架生成器。
/// <para>
/// 旧实现生成的模板有三个问题：脚本引用了不存在的 <c>manifest</c> 变量、
/// 只承诺了浏览器事件而不说明宿主钩子协议、也没有任何可运行的示例。
/// 这里改为生成“能直接跑通一次钩子”的最小项目，并附带协议说明文档。
/// </para>
/// </summary>
public sealed class PluginTemplateGenerator : IPluginTemplateGenerator
{
    private readonly ILogService _log;

    public PluginTemplateGenerator(ILogService log) => _log = log;

    public async Task GenerateAsync(string pluginDir, PluginManifest manifest, CancellationToken ct = default)
    {
        var frontendDir = Path.Combine(pluginDir, "frontend");
        var assetsDir = Path.Combine(frontendDir, "assets");
        var scriptsDir = Path.Combine(pluginDir, "scripts");
        var hooksDir = Path.Combine(pluginDir, "hooks");

        foreach (var dir in new[] { frontendDir, assetsDir, scriptsDir, hooksDir })
        {
            Directory.CreateDirectory(dir);
        }

        var files = new (string Path, string Content)[]
        {
            (Path.Combine(pluginDir, "README.md"), BuildReadme(manifest)),
            (Path.Combine(pluginDir, "PLUGIN_API.md"), BuildApiDoc(manifest)),
            (Path.Combine(pluginDir, "package.json"), BuildPackageJson(manifest)),
            (Path.Combine(frontendDir, "index.html"), BuildIndexHtml(manifest)),
            (Path.Combine(assetsDir, "style.css"), Css),
            (Path.Combine(frontendDir, "plugin.js"), FrontendScript),
            (Path.Combine(scriptsDir, "main.js"), HookEntry),
            (Path.Combine(scriptsDir, "filter.js"), FilterStrategy),
            (Path.Combine(scriptsDir, "lifecycle-start.js"), BuildLifecycleScript("启动")),
            (Path.Combine(scriptsDir, "lifecycle-stop.js"), BuildLifecycleScript("停止")),
            (Path.Combine(hooksDir, "README.md"), HooksReadme)
        };

        foreach (var (path, content) in files)
        {
            await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
        }

        _log.Info($"插件脚手架已生成: {manifest.Name} ({files.Length} 个文件)");
    }

    private static string BuildReadme(PluginManifest manifest) =>
        $"""
        # {manifest.Name}

        > 由 DeEarthX 生成的插件脚手架。所有文件都可以自由修改。

        | 项目 | 值 |
        |------|----|
        | 插件 ID | `{manifest.Id}` |
        | 版本 | `{manifest.Version}` |
        | 作者 | `{manifest.Author}` |

        ## 目录结构

        ```
        manifest.json          插件清单（唯一必需的入口，定义全部能力）
        README.md              本文件
        PLUGIN_API.md          宿主与插件的完整协议说明
        package.json           仅用于本地 node 脚本提示，不参与打包
        frontend/
          index.html           侧边栏页面（sidebarItems.route = "index"）
          plugin.js            页面脚本
          assets/style.css     页面样式
        scripts/
          main.js              钩子入口（manifest.capabilities.hookEntry）
          filter.js            过滤策略示例（可选）
          lifecycle-start.js   插件启用时执行
          lifecycle-stop.js    插件禁用时执行
        ```

        ## 快速开始

        1. 修改 `manifest.json` 里的 `name`、`description`；
        2. 编辑 `frontend/index.html` 定制界面；
        3. 在 `scripts/main.js` 中实现钩子逻辑；
        4. 回到「插件」页面点击 **重新加载**，无需重启应用（宿主默认开启热重载）。

        ## 调试建议

        - 钩子脚本通过 **stdout 输出 JSON** 与宿主通信，日志请写到 **stderr**；
        - 应用日志会记录每个钩子的执行耗时与失败原因；
        - 「插件」页面的诊断接口会列出每个插件声明了哪些钩子、哪些参与了执行。

        ## 打包分发

        把整个插件目录压缩为 zip 即为可分发插件；也可以使用「加密导出」生成 `.dxp`。
        """;

    private static string BuildApiDoc(PluginManifest manifest) =>
        $$$"""
        # 插件协议说明（{{{manifest.Name}}}）

        ## 1. 钩子调用方式

        宿主以 **子进程** 方式运行 `manifest.capabilities.hookEntry`：

        - 按扩展名选择解释器：`.js` / `.mjs` / `.cjs` → `node`，`.py` → `python`，`.bat` / `.cmd` → `cmd`，
          `.ps1` → `pwsh`，`.sh` → `bash`，其它 → 直接执行；
        - **stdin** 收到一行 JSON（调用上下文），读完即可；
        - **stdout** 只应输出结果 JSON（宿主会截取最外层对象，前后可以有其它文本）；
        - **stderr** 用于日志，会转发到应用日志，不影响结果；
        - 每个插件、每次钩子调用都有独立超时，超时会被强制结束并记录，不会卡住整条流水线。

        ### stdin 上下文

        ```json
        {
          "hook": "beforeInstallLoader",
          "pluginId": "{{{manifest.Id}}}",
          "pluginDirectory": "…/plugins/{{{manifest.Id}}}",
          "dataDirectory": "…/plugins/{{{manifest.Id}}}",
          "modpackName": "整合包名",
          "minecraft": "1.20.1",
          "loader": "forge",
          "filePath": "…/instance/整合包名",
          "minecraftVersion": "1.20.1",
          "strategyName": "（过滤策略时）策略名",
          "modId": "（过滤策略时）模组 id",
          "fileName": "（过滤策略时）文件名",
          "settings": { "示例设置项": true },
          "data": "（仅 before/afterModpackProcess、onOutputZip）base64"
        }
        ```

        ### stdout 结果

        ```json
        {
          "success": true,
          "data": "base64（仅数据类钩子，会替换后续插件看到的数据）",
          "dataPath": "out/result.zip（大文件走磁盘，优先于 data）",
          "extra": { "filter": true },
          "error": "失败原因（success=false 时）"
        }
        ```

        > `success` 缺省视为 `true`，因此最小实现只需输出 `{"success":true}`。

        ## 2. 可用的钩子名

        | 名称 | 时机 | 数据 |
        |------|------|------|
        | `beforeModpackProcess` | 整合包解压前 | ✅ 可替换 |
        | `afterModpackProcess` | 整合包解压后 | ✅ 可替换 |
        | `beforeFilterMods` | 模组过滤前 | |
        | `afterFilterMods` | 模组过滤后 | |
        | `beforeInstallLoader` | 安装模组加载器前 | |
        | `afterInstallLoader` | 安装模组加载器后 | |
        | `onOutputZip` | 输出压缩包前 | ✅ 可替换 |
        | `filter_strategy` | 每个模组过滤判定 | 返回 `extra.filter = true` 表示过滤 |
        | `onError` | 流程出错 | |

        > 在 `manifest.json` 的 `capabilities.hooks` 中声明需要接收的钩子名。
        > 未声明的钩子不会调用你的插件——这是性能优化，而不是安全边界。

        ## 3. 过滤策略

        ```json
        "capabilities": {
          "hooks": ["filter_strategy"],
          "filterStrategies": [
            { "name": "my-filter", "type": "builtin", "label": "我的过滤规则" }
          ]
        }
        ```

        命中时输出：

        ```json
        { "success": true, "extra": { "filter": true } }
        ```

        ## 4. 前端页面

        页面文件放在 `frontend/` 下，宿主通过 `/plugin-page/<pluginId>/<pageKey>` 提供：

        - `frontend/index.html` ← `sidebarItems[].route = "index"`；
        - `frontend/<key>.html` 或 `frontend/<key>/index.html` 对应其它 key；
        - 页面运行在沙箱 iframe 中，可通过 `window.parent.postMessage` 与应用通信；
        - 页面引用的静态资源放到 `frontend/` 下，用相对路径引用即可。

        侧边栏入口在 `manifest.json` 中声明：

        ```json
        "capabilities": { "sidebar": true },
        "sidebarItems": [
          { "key": "main", "label": "我的插件", "icon": "🧩", "route": "index" }
        ]
        ```

        ## 5. 生命周期

        `manifest.lifecycle.start` / `lifecycle.stop` 会在插件启用 / 禁用时调用，
        适合做预热、缓存清理等收尾工作。它们同样通过 stdin 收到 JSON，stdout 被忽略。

        ## 6. 配置项

        `defaultConfig` 定义配置键与默认值，用户可在插件详情页修改并保存到宿主：

        ```json
        "defaultConfig": { "debug": true, "maxThreads": 4 },
        "configLabels": { "debug": "调试模式" },
        "configDescriptions": { "maxThreads": "并发下载线程数" }
        ```

        钩子上下文的 `settings` 字段即为当前生效的配置。

        ## 7. 依赖与版本

        ```json
        "dependencies": [
          { "id": "1234567890", "version": ">=1.2.0" },
          { "id": "optional-plugin", "optional": true }
        ]
        ```

        未满足的硬依赖会导致插件被跳过加载，原因会出现在诊断接口中。
        """;

    private static string BuildPackageJson(PluginManifest manifest) =>
        $$"""
        {
          "name": "deearthx-plugin-{{manifest.Id}}",
          "version": "{{manifest.Version}}",
          "private": true,
          "type": "module",
          "description": "{{Escape(manifest.Name)}} — DeEarthX 插件",
          "scripts": {
            "dry-run": "node scripts/main.js"
          }
        }
        """;

    private static string BuildIndexHtml(PluginManifest manifest) =>
        $$"""
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width, initial-scale=1.0">
          <title>{{Escape(manifest.Name)}}</title>
          <link rel="stylesheet" href="assets/style.css">
        </head>
        <body>
          <main class="plugin">
            <header class="plugin__header">
              <h1>{{Escape(manifest.Name)}}</h1>
              <p class="plugin__meta">{{Escape(manifest.Author ?? "Unknown")}} · v{{manifest.Version}}</p>
            </header>

            <section class="plugin__card">
              <h2>运行状态</h2>
              <p id="status">正在初始化…</p>
              <button id="ping" type="button">测试与宿主通信</button>
            </section>
          </main>

          <script type="module" src="plugin.js"></script>
        </body>
        </html>
        """;

    private const string Css =
        """
        :root {
          color-scheme: light dark;
          font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'PingFang SC', sans-serif;
        }

        body {
          margin: 0;
          background: transparent;
        }

        .plugin {
          padding: 24px;
          max-width: 720px;
        }

        .plugin__header h1 {
          margin: 0 0 4px;
          font-size: 20px;
        }

        .plugin__meta {
          margin: 0;
          font-size: 12px;
          opacity: 0.6;
        }

        .plugin__card {
          margin-top: 20px;
          padding: 16px;
          border: 1px solid rgba(128, 128, 128, 0.25);
          border-radius: 12px;
        }

        .plugin__card h2 {
          margin: 0 0 8px;
          font-size: 14px;
        }

        button {
          margin-top: 8px;
          padding: 6px 14px;
          border-radius: 8px;
          border: 1px solid rgba(128, 128, 128, 0.35);
          background: transparent;
          cursor: pointer;
        }
        """;

    private const string FrontendScript =
        """
        // 插件前端入口。页面运行在沙箱 iframe 中，可以自由使用浏览器能力。
        const status = document.getElementById('status');
        const ping = document.getElementById('ping');

        status.textContent = `插件页面已加载（${new Date().toLocaleTimeString()}）`;

        // 与应用主窗口通信：消息 type 使用 deearthx: 前缀避免冲突。
        ping?.addEventListener('click', () => {
          window.parent?.postMessage({ type: 'deearthx:plugin-ping', at: Date.now() }, '*');
          status.textContent = '已向宿主发送 ping，可在应用控制台查看。';
        });

        // 接收宿主事件（主窗口会在 window 上派发 deearthx:event）。
        window.addEventListener('message', (event) => {
          const data = event.data;
          if (data && typeof data === 'object' && String(data.type).startsWith('deearthx:')) {
            console.debug('[plugin] host event', data);
          }
        });
        """;

    private const string HookEntry =
        """
        // 插件钩子入口。
        // 宿主在 stdin 写入一行 JSON；脚本在 stdout 输出一行结果 JSON。
        // 注意：stdout 只能出现结果 JSON；日志请用 console.error（走 stderr）。

        const chunks = [];
        for await (const chunk of process.stdin) {
          chunks.push(chunk);
        }

        const raw = Buffer.concat(chunks).toString('utf8').trim();
        const context = raw ? JSON.parse(raw) : {};

        console.error(`[plugin] hook=${context.hook} modpack=${context.modpackName ?? '-'}`);

        // 在这里实现你的逻辑。
        switch (context.hook) {
          case 'beforeInstallLoader':
            // 例如：提前准备加载器需要的本地文件
            break;

          case 'afterFilterMods':
            // 例如：把过滤结果上报到自己的服务
            break;

          default:
            break;
        }

        // 输出结果。不需要传递数据时，写一个最小的 { success: true } 即可。
        // 需要替换后续插件看到的数据时使用 data（base64）或 dataPath（相对插件目录的文件）。
        process.stdout.write(JSON.stringify({ success: true }));
        """;

    private const string FilterStrategy =
        """
        // 过滤策略示例：在 manifest.capabilities.filterStrategies 中声明后生效。
        // 输入上下文包含 fileName / filePath / modId / strategyName 等字段。
        // 返回 { extra: { filter: true } } 表示该模组应被过滤。

        const chunks = [];
        for await (const chunk of process.stdin) {
          chunks.push(chunk);
        }

        const context = JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}');
        const name = String(context.fileName ?? '').toLowerCase();

        // 示例规则：文件名包含 "client" 的模组判定为客户端专用模组。
        const shouldFilter = name.includes('client');

        console.error(`[plugin] filter ${context.fileName} -> ${shouldFilter}`);

        process.stdout.write(JSON.stringify({
          success: true,
          extra: { filter: shouldFilter }
        }));
        """;

    private static string BuildLifecycleScript(string phase) =>
        $$"""
        // 插件{{phase}}脚本：stdin 收到 { hook, pluginId, pluginDirectory }，stdout 被忽略。
        const chunks = [];
        for await (const chunk of process.stdin) {
          chunks.push(chunk);
        }

        const context = JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}');
        console.error('[plugin] lifecycle {{phase}} hook=' + context.hook + ' id=' + context.pluginId);
        """;

    private const string HooksReadme =
        """
        # hooks 目录

        这里可以放钩子的附加配置片段。真正的入口文件由 `manifest.json` 的
        `capabilities.hookEntry` 指定（脚手架指向 `scripts/main.js`）。

        约定：一个钩子进程只处理一次调用，日志走 stderr，结果 JSON 走 stdout。
        详见上级目录的 `PLUGIN_API.md`。
        """;

    private static string Escape(string value) => JsonEncodedText.Encode(value).ToString();
}
