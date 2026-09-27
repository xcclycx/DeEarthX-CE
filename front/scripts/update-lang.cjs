/**
 * 一次性语言包维护脚本。
 *
 * 作用：
 *  1. 删除已废弃的 menu.server 与整个 server 段落（「我的服务端」页面已移除）。
 *  2. 重写提到「AI 守护进程」的文案。
 *  3. 为「设置 → 日志」「程序错误提醒」「导出优化」补充缺失的键（英文与其它语言缺失时回退到简体中文）。
 *
 * 用法：node scripts/update-lang.cjs
 */
const fs = require('fs');
const path = require('path');

const langDir = path.join(__dirname, '..', 'lang');
const zhCn = JSON.parse(fs.readFileSync(path.join(langDir, 'zh_cn.json'), 'utf8'));

// --- 新增键：以 zh_cn 为基线，其它语言若未提供则回退 ---
const settingAdditions = {
  category_optimize: 'Export Optimization',
  optimize_enabled_name: 'Enable export optimization',
  optimize_enabled_desc: 'Run the optimization pipeline after production (Aikar\'s Flags, launch scripts, resource stripping)',
  optimize_script_name: 'Generate launch scripts',
  optimize_script_desc: 'Generate start.bat (Windows) and start.sh (Linux/macOS)',
  optimize_aikar_name: 'Inject Aikar\'s Flags',
  optimize_aikar_desc: 'Apply Minecraft-specific G1GC tuning; switches to the high-memory profile above 12GB',
  optimize_strip_name: 'Strip client resources',
  optimize_strip_desc: 'Remove client-only directories such as resourcepacks, shaderpacks, saves and logs',
  optimize_sensitive_name: 'Sanitize sensitive data',
  optimize_sensitive_desc: 'Remove options.txt, servers.dat and account token files that may leak personal data',
  optimize_redundant_name: 'Detect redundant files',
  optimize_redundant_desc: 'Detect duplicate mods, leftover .disabled files and duplicate libraries',
  optimize_props_name: 'Generate server.properties',
  optimize_props_desc: 'Create an optimized server.properties when missing (never overwrites an existing file)',
  optimize_backup_name: 'Back up before changes',
  optimize_backup_desc: 'Copy affected files into backup/ before touching them (strongly recommended)',
  optimize_dryrun_name: 'Analyze only (dry-run)',
  optimize_dryrun_desc: 'Produce a preview report without modifying any file',
  optimize_memory_title: 'Server memory allocation',
  optimize_memory_desc: '0 means auto-detect (half of system memory, clamped to 2GB–12GB). -Xms always matches -Xmx.',
  optimize_memory_auto: 'Auto',
  optimize_memory_current: 'Currently using {memory}MB{auto}',
  optimize_flags_preview: 'JVM arguments preview',
  optimize_flags_high_memory: 'High-memory profile enabled (>12GB)',
  optimize_flags_loading: 'Loading arguments...',
  optimize_flags_failed: 'Failed to read JVM arguments',
  optimize_preview_button: 'Preview optimization',
  optimize_preview_title: 'Optimization preview (dry-run)',
  optimize_preview_desc: 'Enter a server directory; nothing will be modified',
  optimize_preview_path_placeholder: 'e.g. C:\\Servers\\MyServer',
  optimize_preview_ok: 'Preview complete, no files were modified',
  optimize_preview_failed: 'Preview failed',
  optimize_preview_path_required: 'Please enter a server directory',
  optimize_report_generated_scripts: 'Generated launch scripts',
  optimize_report_removed: 'Removed client resources',
  optimize_report_sensitive: 'Sanitized sensitive files',
  optimize_report_redundancy: 'Redundant files',
  optimize_report_config_changes: 'Config changes',
  optimize_report_suggestions: 'Manual follow-up',
  optimize_report_gains: 'Estimated performance gains',
  optimize_report_warnings: 'Warnings',
  optimize_report_logs: 'Operation log',
  optimize_report_backup: 'Backup directory',
  optimize_report_empty: 'Nothing to process',
  optimize_report_dryrun_badge: 'Preview mode',
  optimize_report_memory: 'Heap memory',
  optimize_report_none: 'None',
  logs_title: 'Logs',
  logs_desc: 'Inspect runtime logs, filter and export them for troubleshooting',
  logs_refresh: 'Refresh',
  logs_export: 'Export logs',
  logs_export_all: 'Export all logs',
  logs_export_filtered: 'Export filtered result',
  logs_clear: 'Clean up logs',
  logs_clear_retention: 'Apply retention policy',
  logs_clear_all: 'Delete all logs',
  logs_clear_confirm: 'Delete all log files? This cannot be undone.',
  logs_cleared: 'Cleaned up {count} log file(s)',
  logs_clear_failed: 'Failed to clean up logs',
  logs_open_folder: 'Open log folder',
  logs_open_folder_success: 'Log folder opened',
  logs_open_folder_failed: 'Failed to open log folder',
  logs_export_all_success: 'All logs exported',
  logs_export_filtered_success: 'Filtered result exported',
  logs_export_failed: 'Failed to export logs',
  logs_load_failed: 'Failed to load logs',
  logs_loading: 'Loading logs...',
  logs_empty: 'No log entries',
  logs_empty_hint: 'Logs will appear here once the app runs',
  logs_level: 'Level',
  logs_level_all: 'All levels',
  logs_level_debug: 'Debug',
  logs_level_info: 'Info',
  logs_level_warn: 'Warning',
  logs_level_error: 'Error',
  logs_keyword: 'Keyword',
  logs_keyword_placeholder: 'Search log content...',
  logs_file: 'Log file',
  logs_all_files: 'Current file',
  logs_line_count: 'Showing {shown} of {total} lines',
  logs_truncated: 'Log is long; showing the latest {max} lines. Export to see everything.',
  logs_scanned_files: 'Scanned {count} file(s)',
  logs_directory: 'Log directory',
  errors_title: 'Program errors',
  errors_desc: 'Recent errors and warnings',
  errors_empty: 'No errors recorded',
  errors_ack: 'Mark as read',
  errors_ack_all: 'Mark all as read',
  errors_ack_success: 'Marked {count} record(s)',
  errors_clear: 'Clear records',
  errors_clear_success: 'Error records cleared',
  errors_load_failed: 'Failed to read error records',
  errors_detail: 'Details',
  errors_unread_count: '{count} unread error(s)',
  errors_bell_title: 'Program error alerts',
  java_title: 'Java detection',
  java_desc: 'Detect available Java runtimes; the server needs Java 17 or newer',
  java_detect_button: 'Detect',
  java_detecting: 'Detecting...',
  java_detect_failed: 'Java detection failed',
  java_not_found: 'No Java found. Please install Java 21 or newer and retry.',
  java_available_count: 'Found {count} Java runtime(s)',
  java_vendor: 'Vendor',
  java_version: 'Version',
  java_path: 'Path'
};

const homeTip3 = {
  en_us: "After completion, check the full runtime log under 'Settings → Logs'; optimization results are in deearthx-optimization-report.json inside the server directory",
  zh_tw: '製作完成後可在「設定 → 日誌」中查看完整執行日誌，優化結果見服務端目錄下的 deearthx-optimization-report.json',
  zh_hk: '製作完成後可在「設定 → 日誌」中查看完整執行日誌，優化結果見服務端目錄下的 deearthx-optimization-report.json',
  ja_jp: '完成後は「設定 → ログ」で実行ログを確認できます。最適化結果はサーバーディレクトリの deearthx-optimization-report.json にあります',
  fr_fr: "Une fois terminé, consultez le journal complet dans « Paramètres → Journaux » ; le rapport d'optimisation se trouve dans deearthx-optimization-report.json",
  de_de: 'Nach Abschluss finden Sie das vollständige Protokoll unter „Einstellungen → Protokolle“; der Optimierungsbericht liegt in deearthx-optimization-report.json',
  es_es: 'Al finalizar, consulta el registro completo en «Ajustes → Registros»; el informe de optimización está en deearthx-optimization-report.json'
};

const homeFeature4 = {
  en_us: "One-click launch scripts with Aikar's Flags and an optimization report",
  zh_tw: "一鍵開服，自動產生 Aikar's Flags 啟動腳本與優化報告",
  zh_hk: "一鍵開服，自動產生 Aikar's Flags 啟動腳本與優化報告",
  ja_jp: "ワンクリックで Aikar's Flags 起動スクリプトと最適化レポートを自動生成",
  fr_fr: "Scripts de lancement Aikar's Flags et rapport d'optimisation générés automatiquement",
  de_de: "Startskripte mit Aikar's Flags und Optimierungsbericht automatisch erzeugen",
  es_es: "Scripts de inicio con Aikar's Flags e informe de optimización automáticos"
};

const files = fs.readdirSync(langDir).filter((f) => f.endsWith('.json'));
const report = [];

for (const file of files) {
  const full = path.join(langDir, file);
  const data = JSON.parse(fs.readFileSync(full, 'utf8'));
  const locale = path.basename(file, '.json');
  const changes = [];

  // 1) 删除废弃段落
  if (data.menu && Object.prototype.hasOwnProperty.call(data.menu, 'server')) {
    delete data.menu.server;
    changes.push('removed menu.server');
  }
  if (Object.prototype.hasOwnProperty.call(data, 'server')) {
    delete data.server;
    changes.push('removed server section');
  }

  // 2) 文案重写
  if (data.home) {
    if (homeFeature4[locale]) {
      data.home.software_intro_feature4 = homeFeature4[locale];
      changes.push('rewrote feature4');
    } else if (typeof data.home.software_intro_feature4 === 'string') {
      data.home.software_intro_feature4 = data.home.software_intro_feature4
        .replace(/AI\s*守护进程.*$/, "Aikar's Flags 启动脚本与优化报告")
        .replace(/AI\s*guardian.*$/i, "Aikar's Flags launch scripts and an optimization report");
      changes.push('rewrote feature4 (fallback)');
    }
    if (homeTip3[locale]) {
      data.home.software_tip3 = homeTip3[locale];
      changes.push('rewrote tip3');
    }
  }

  // 3) 补充新键（以 zh_cn 为基线，非中文语言用上面的英文/本地化文本）
  if (!data.setting) {
    data.setting = {};
  }
  for (const [key, fallback] of Object.entries(settingAdditions)) {
    if (data.setting[key] !== undefined) {
      continue;
    }
    // 英文文件用英文文本；其它语言目前回退到简体中文，保证界面不出现 key。
    data.setting[key] = locale === 'en_us' ? fallback : zhCn.setting[key];
  }
  changes.push('added setting keys');

  // 键顺序保持：重建对象，让常见分组顺序一致
  const ordered = {};
  for (const key of ['common', 'menu', 'home', 'template', 'setting', 'about', 'galaxy', 'message', 'plugin', 'download']) {
    if (data[key] !== undefined) {
      ordered[key] = data[key];
    }
  }
  for (const key of Object.keys(data)) {
    if (ordered[key] === undefined) {
      ordered[key] = data[key];
    }
  }

  fs.writeFileSync(full, JSON.stringify(ordered, null, 2) + '\n', 'utf8');
  report.push(`${file}: ${changes.join(', ')}`);
}

console.log(report.join('\n'));
