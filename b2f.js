import fs from "node:fs";
import archiver from "archiver";
import path from "node:path";

const args = process.argv.slice(2);

if (args.length !== 1) {
    console.error("使用方法: node b2f.js <b2f|b2r|b2t>");
    process.exit(1);
}

/**
 * 复制 sourceDir 下所有文件到 destDir（跳过 appsettings.*.json 等非运行时必需的配置）
 * .deps.json / .runtimeconfig.json / appsettings.json 等 .NET 运行时必需文件会保留
 * exeFilename 指定的文件会被重命名为 exeRename（用于 Tauri externalBin 命名）
 */
function copyBackendFiles(sourceDir, destDir, exeRename = null, exeFilename = "core.exe") {
    if (!fs.existsSync(sourceDir)) {
        console.error(`错误: 源目录不存在: ${sourceDir}`);
        process.exit(1);
    }

    if (!fs.existsSync(destDir)) {
        fs.mkdirSync(destDir, { recursive: true });
        console.log(`创建目录: ${destDir}`);
    }

    const skipFiles = new Set(["appsettings.Development.json"]);

    let count = 0;
    for (const file of fs.readdirSync(sourceDir)) {
        if (skipFiles.has(file)) continue;
        const src = path.join(sourceDir, file);
        if (!fs.statSync(src).isFile()) continue;

        let destName = file;
        if (exeRename && file === exeFilename) {
            destName = exeRename;
        }

        fs.copyFileSync(src, path.join(destDir, destName));
        count++;
    }
    console.log(`复制 ${count} 个文件: ${sourceDir} -> ${destDir}`);
}

switch (args[0]) {
    case "b2f": //backend to frontend (binaries/)
        copyBackendFiles(
            "./backend-net/publish",
            "./front/src-tauri/binaries",
            "core-x86_64-pc-windows-msvc.exe",
            "core.exe"
        );
        break;
    case "b2t": //backend to target/release/
        copyBackendFiles(
            "./backend-net/publish",
            "./front/src-tauri/target/release"
        );
        break;
    case "b2r": //build to root
        const innoDir = "./front/src-tauri/target/release/bundle/inno";
        const exePath = "./front/src-tauri/target/release/bundle/inno/DeEarthX-V4-Setup-4.0.1-beta.exe";
        const rootExePath = "./DeEarthX-V4_x64-setup.exe";
        const zipPath = "./DeEarthX-V4_x64-setup.zip";

        if (!fs.existsSync(exePath)) {
            console.error(`错误: 源文件不存在: ${exePath}`);
            console.error("请先运行 Inno Setup 编译生成安装包");
            process.exit(1);
        }

        // 移动 exe 到根目录
        fs.renameSync(exePath, rootExePath);
        console.log(`移动文件: ${exePath} -> ${rootExePath}`);

        // 打包成 zip
        const output = fs.createWriteStream(zipPath);
        const archive = archiver("zip", {
            zlib: { level: 9 } // 最高压缩级别
        });

        output.on('close', () => {
            console.log(`打包完成: ${zipPath} (${archive.pointer()} 字节)`);
        });

        archive.pipe(output);
        archive.file(rootExePath, { name: path.basename(rootExePath) });
        await archive.finalize();

        break;
    default:
        console.error(`错误: 未知参数 '${args[0]}'`);
        console.error("有效参数: b2f, b2r");
        process.exit(1);
}
