# DeEarthX V4

[简体中文](README.md) | English

<div align="center">

<img src="front/public/dex.png" height="80" width="80" alt="DeEarthX Logo"/>

**One-click Minecraft modpack to runnable server converter**

<br/>

[![Github release](https://img.shields.io/github/v/tag/xcclycs/DeEarthX-CE?style=for-the-badge&logo=github&label=Release)](https://github.com/xcclycs/DeEarthX-CE/releases)
[![GitHub](https://img.shields.io/github/license/xcclycs/DeEarthX-CE?style=for-the-badge&color=blue)](https://github.com/xcclycs/DeEarthX-CE/blob/main/LICENSE)
[![GitHub last commit](https://img.shields.io/github/last-commit/xcclycs/DeEarthX-CE?style=for-the-badge&color=orange&label=Last%20Commit)](https://github.com/xcclycs/DeEarthX-CE/commits/main)

<br/>

<a href="https://qm.qq.com/q/REPt0kcuYu">
  <img src="front/public/QQ.png" height="16" width="16" alt="QQ"/>
  QQ Group
</a>
&nbsp;&nbsp;·&nbsp;&nbsp;
<a href="https://www.bilibili.com/video/BV1xXwRzpEMh">
  <img src="front/public/bilibili.svg" height="16" width="16" alt="Bilibili"/>
  Promo Video
</a>

</div>

---

> [!WARNING]
> Mods may not be filtered completely, and the generated server is **prohibited from being sold**!

---

## Project Overview

DeEarthX V4 is a **Windows desktop application** that helps you quickly convert client-side modpacks into runnable servers. Drag in a modpack file, select a mode, and get an out-of-the-box server — no manual configuration needed.

---

## Core Features

<table>
<tr>
<td width="50%">

### Modpack Support

| Format | Status |
|--------|--------|
| CurseForge | ✅ |
| Modrinth | ✅ |
| MCBBS | ✅ |
| MultiMC Pack | ❌ |

</td>
<td width="50%">

### Mod Loaders

| Loader | Status |
|--------|--------|
| Forge | ✅ |
| NeoForge | ✅ |
| Fabric | ✅ |
| Quilt | ❌ |

</td>
</tr>
</table>

### Smart Mod Filtering

Automatically distinguish **client-side** and **server-side** mods using a multi-strategy system:

| Priority | Strategy | Description |
|----------|----------|-------------|
| Equal | **Dexpub** (Galaxy Square) | Community-maintained database, judges both client & server mods |
| Equal | **Hash + Modrinth** | Parallel hash matching and Modrinth API query |
| Equal | **Mcmod** | Query mcmod.cn for mod classification |
| Equal | **Mixin** | Mixin config analysis for unjudged mods |

### Working Modes

| Mode | Description |
|------|-------------|
| **Server Mode** | Full process — download server jar, mod loader, and filter mods |
| **Upload Mode** | Mod filtering only — no server file downloads |

### Mirror Acceleration

Built-in download mirrors:

- **BMCLAPI** — configurable (`on` / `off`)
- **MCIMirror** — configurable (`on` / `off` / `partial`)

### Multi-Language

| Language | Code |
|----------|------|
| 简体中文 | `zh-CN` |
| 繁體中文 (香港) | `zh-HK` |
| 繁體中文 (台灣) | `zh-TW` |
| English | `en-US` |

---

## Technical Architecture

<table>
<tr>
<th>Backend</th>
<th>Frontend</th>
<th>Packaging</th>
</tr>
<tr>
<td>

- C# (.NET 10)
- ASP.NET Core
- Socket.IO (custom protocol)
- Tomlyn (TOML parsing)
- Single-file publish → `core.exe`

</td>
<td>

- Vue 3 (Composition API)
- TypeScript
- Tauri 2 (Rust)
- Ant Design Vue
- Tailwind CSS
- Vue I18n

</td>
<td>

- dotnet publish (backend)
- Tauri CLI (frontend)
- Inno Setup 6 (installer)
- UPX (EXE compression)

</td>
</tr>
</table>

---

## Project Structure

```
DeEarthX-CE/
├── backend-net/              # .NET backend
│   ├── src/
│   │   ├── DeEarthX.Web/     # ASP.NET Core entry (port 37019)
│   │   ├── DeEarthX.Core/    # Core abstractions & config
│   │   ├── DeEarthX.Dearth/  # Mod filtering engine
│   │   ├── DeEarthX.Dex/     # Dex service
│   │   ├── DeEarthX.Galaxy/  # Galaxy service
│   │   ├── DeEarthX.Guardian/# AI crash detection & safe execution
│   │   ├── DeEarthX.Infrastructure/ # Infra (download/crypto/Java/ZIP)
│   │   ├── DeEarthX.ModLoader/      # Mod loaders (Forge/Fabric/NeoForge)
│   │   ├── DeEarthX.Platform/       # Platform adapters (CurseForge/Modrinth)
│   │   ├── DeEarthX.Plugins/        # Plugin system
│   │   ├── DeEarthX.Realtime/       # Real-time communication (Socket.IO)
│   │   └── DeEarthX.Templates/      # Template management
│   └── publish/              # Build output
├── front/                    # Tauri + Vue frontend
│   ├── src/                  # Vue source
│   ├── src-tauri/            # Rust/Tauri source
│   │   ├── installer/        # Inno Setup script
│   │   └── binaries/         # Backend EXE/DLL (copied at build)
│   └── scripts/              # Build scripts
├── IS6/                      # Inno Setup 6 compiler
├── .build/                   # Build tools (UPX etc.)
├── b2f.js                    # Build helper script
└── package.json              # Root build config
```

---

## Installation

1. Download the latest installer from [Releases](https://github.com/xcclycs/DeEarthX-CE/releases)
2. Run the installer
3. Launch DeEarthX — ready to use

> **Tip:** Avoid installing on drive C to prevent permission issues.

---

## System Requirements

| Requirement | Server Mode | Upload Mode |
|-------------|:-----------:|:-----------:|
| Windows OS | ✅ | ✅ |
| Java Runtime | ✅ | ❌ |

**Supported Minecraft versions:** 1.16.5 → Latest

---

## Development

### Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 10.0+ |
| Node.js | 24+ |
| pnpm | 9+ |
| Rust | stable |
| Inno Setup 6 | Bundled in `IS6/` |

### Development Commands

```bash
# Install frontend dependencies
pnpm install

# Frontend dev server (port 9888)
cd front && pnpm run dev

# Tauri dev mode (Vite + Tauri window)
cd front && pnpm run tauri-dev

# Backend dev mode (port 37019)
cd backend-net && dotnet run --project src/DeEarthX.Web
```

### Production Build

```bash
# Full build (backend → UPX → Tauri → Inno Setup → installer)
pnpm run build
```

Build pipeline:

1. `backend` — dotnet publish compiles .NET backend
2. `rename-backend` — DeEarthX.Web.exe → core.exe
3. `upx` — UPX compresses core.exe
4. `back2front` — Copy backend files to Tauri binaries/
5. `tauri` — Tauri builds frontend app
6. `back2target` — Copy backend files to target/release/
7. `inno` — Inno Setup compiles installer
8. `build2root` — Move installer to root and create zip

---

## Development Team

<table>
<tr>
<td align="center" width="50%">
  <img src="front/public/tianpao.jpg" width="80" height="80" style="border-radius:50%" alt="Tianpao"/><br/>
  <b>Tianpao</b><br/>
  <sub>Core Development</sub>
</td>
<td align="center" width="50%">
  <img src="front/public/xcc.jpg" width="80" height="80" style="border-radius:50%" alt="Community Version Developer"/><br/>
  <b>Community Version Developer</b><br/>
  <sub>CE Core Development / Feature Optimization</sub>
</td>
</tr>
</table>

---

<div align="center">

**DeEarthX V4** — by [xcclycs](https://github.com/xcclycs) × [Tianpao](https://github.com/Tianpao)

</div>
