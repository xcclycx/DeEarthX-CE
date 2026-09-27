/**
 * DeEarthX V4 - Inno Setup 构建脚本
 *
 * 用法：
 *   node scripts/build-installer.js
 *
 * 流程：
 *   1. 定位 IS6/ISCC.exe（项目根目录下的 Inno Setup 6）
 *   2. 验证 Tauri 构建产物存在
 *   3. 调用 ISCC.exe 编译 deearthx.iss（使用相对路径）
 *   4. 输出最终的 Inno Setup 安装包
 */

const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

// ── 配置 ──────────────────────────────────────────────────
const PROJECT_ROOT = path.resolve(__dirname, '..', '..');

const CONFIG = {
  isccPath: path.resolve(PROJECT_ROOT, 'IS6', 'ISCC.exe'),
  issFile: path.resolve(__dirname, '..', 'src-tauri', 'installer', 'deearthx.iss'),
  sourceDir: path.resolve(__dirname, '..', 'src-tauri', 'target', 'release'),
};

// ── 主流程 ────────────────────────────────────────────────
function main() {
  console.log('=== DeEarthX V4 Inno Setup 构建 ===\n');

  // Step 1: 验证 ISCC.exe 存在
  if (!fs.existsSync(CONFIG.isccPath)) {
    console.error(`[ERROR] 未找到 ISCC.exe: ${CONFIG.isccPath}`);
    console.error('请确保 IS6 文件夹位于项目根目录。');
    process.exit(1);
  }
  console.log(`[OK] ISCC.exe: ${CONFIG.isccPath}`);

  // Step 2: 验证源文件存在
  const exePath = path.join(CONFIG.sourceDir, 'dex-v4-ui.exe');
  if (!fs.existsSync(exePath)) {
    console.error(`[ERROR] 未找到构建产物: ${exePath}`);
    console.error('请先运行 tauri build 生成 .exe 文件。');
    process.exit(1);
  }
  console.log(`[OK] 主程序: ${exePath}`);

  // Step 3: 验证 .iss 脚本存在
  if (!fs.existsSync(CONFIG.issFile)) {
    console.error(`[ERROR] 未找到 .iss 脚本: ${CONFIG.issFile}`);
    process.exit(1);
  }
  console.log(`[OK] 安装脚本: ${CONFIG.issFile}`);

  // Step 4: 确保输出目录存在
  const outputDir = path.resolve(path.dirname(CONFIG.issFile), '..', 'target', 'release', 'bundle', 'inno');
  if (!fs.existsSync(outputDir)) {
    fs.mkdirSync(outputDir, { recursive: true });
  }

  // Step 5: 调用 ISCC 编译（.iss 内部使用相对路径，无需传参）
  const iscc = CONFIG.isccPath.includes(' ') ? `"${CONFIG.isccPath}"` : CONFIG.isccPath;
  const cmd = `${iscc} "${CONFIG.issFile}"`;

  console.log(`\n[BUILD] 正在编译 Inno Setup 安装包...`);
  console.log(`[CMD] ${cmd}\n`);

  try {
    execSync(cmd, { stdio: 'inherit', cwd: path.dirname(CONFIG.issFile) });
    console.log('\n=== 构建完成！ ===');
    console.log(`输出目录: ${outputDir}`);
  } catch (err) {
    console.error('\n[ERROR] Inno Setup 编译失败！');
    console.error(err.message);
    process.exit(1);
  }
}

main();