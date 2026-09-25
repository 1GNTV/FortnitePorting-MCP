<div align="center">

# Fortnite Porting MCP

[![Discord](https://img.shields.io/discord/866821077769781249?logo=discord&logoColor=white&label=Discord&color=7289da)](https://discord.gg/fortniteporting)
[![Blender](https://img.shields.io/badge/Blender-4.2+-blue?logo=blender&logoColor=white&color=orange)](https://www.blender.org/download/)
[![Unreal](https://img.shields.io/badge/Unreal-5.8-blue?logo=unreal-engine&logoColor=white&color=white)](https://www.unrealengine.com/en-US/download)

<img alt="Fortnite Porting" src=".github/cover.png" />

</div>

This fork adds a local **Model Context Protocol (MCP)** server to Fortnite Porting so an AI client can search, inspect, browse, and export Fortnite assets through the same CUE4Parse data and export pipeline used by the app.

> [!IMPORTANT]
> This repository is a fork of [h4lfheart/FortnitePorting](https://github.com/h4lfheart/FortnitePorting). Credit for Fortnite Porting itself goes to the original project and its contributors. This fork adds the MCP layer.

## Features

- **Browse Fortnite assets** - Explore cosmetics, props, gameplay items, and more.
- **Browse the real Fortnite file tree** - Navigate the same virtual file system used by the **Files** tab.
- **Search raw Fortnite files** - Search .uasset, .umap, and .ufont paths directly.
- **Inspect Unreal packages** - List package objects and raw asset properties.
- **Export through AI tools** - Send found assets to Blender, Unreal Engine, or the Assets Folder.
- **Use normal Fortnite Porting too** - The existing UI and export workflow remain available.
- **Local MCP server** - Runs on 127.0.0.1 by default and is only reachable from the local machine.

# MCP Setup

## 1. Requirements

You need:

- **Windows x64**
- **Fortnite Porting MCP** from this repository
- Either a local Fortnite installation or Fortnite Porting **On-Demand** mode
- An MCP-compatible AI client
- **Blender** and/or **Unreal Engine** only if you want live export directly into those applications

For live export:

- Blender requires the Fortnite Porting Blender companion plugin.
- Unreal Engine requires the Fortnite Porting / UEFormat companion plugins.
- These plugins are installed from Fortnite Porting's **Plugin** tab.

You do **not** need Blender or Unreal Engine if you only want to search, inspect, browse, or export files to the Assets Folder.

## 2. Install Fortnite Porting MCP

### Option A - Download a prebuilt Actions artifact

1. Open this repository on GitHub.
2. Go to **Actions**.
3. Open the latest successful **Build Commit** workflow on the main branch.
4. Download the generated FortnitePorting-<commit> artifact.
5. Extract the archive.
6. Launch FortnitePorting.exe.

GitHub Actions artifacts expire after GitHub's configured retention period, so if no current artifact is available, build from source using the instructions below.

### Option B - Build from source

Install the **.NET 10 SDK**, then clone this fork with all submodules:

~~~powershell
git clone --recursive https://github.com/1GNTV/FortnitePorting-MCP.git
cd FortnitePorting-MCP
~~~

Restore and publish:

~~~powershell
dotnet restore ./src/FortnitePorting
dotnet publish ./src/FortnitePorting -c Release --self-contained -r win-x64 -o "./Release" `
  -p:PublishSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -p:IncludeNativeLibrariesForSelfExtract=true
~~~

The executable will be placed in:

~~~text
./Release/FortnitePorting.exe
~~~

## 3. Complete Fortnite Porting setup

Launch FortnitePorting.exe and finish the normal Fortnite Porting setup first.

Choose one of the supported data sources:

- **Latest Installed**
- **On-Demand**
- **Custom**

Wait until Fortnite Porting has finished loading its Fortnite data.

The MCP server starts automatically with the application.

> [!IMPORTANT]
> Keep FortnitePorting.exe open while using the MCP server.

## 4. Verify that the MCP server is running

The default endpoints are:

~~~text
MCP:    http://127.0.0.1:6010/mcp
Health: http://127.0.0.1:6010/health
~~~

Test the health endpoint from Command Prompt or PowerShell:

~~~powershell
curl http://127.0.0.1:6010/health
~~~

A working server returns JSON similar to:

~~~json
{
  "service": "FortnitePorting-MCP",
  "mcp": "http://127.0.0.1:6010/mcp",
  "fortniteReady": true,
  "registryAssets": 557759
}
~~~

The exact asset count varies by Fortnite version and loaded data.

If fortniteReady is false, let Fortnite Porting finish loading before using MCP tools.

## 5. Add the MCP server to your AI client

Generic Streamable HTTP configuration:

~~~json
{
  "mcpServers": {
    "fortnite-porting": {
      "type": "http",
      "url": "http://127.0.0.1:6010/mcp"
    }
  }
}
~~~

Some MCP clients infer the transport automatically and do not accept the type field. In that case use:

~~~json
{
  "mcpServers": {
    "fortnite-porting": {
      "url": "http://127.0.0.1:6010/mcp"
    }
  }
}
~~~

Restart or reconnect your MCP client after changing its configuration.

## 6. Test the connection

Try:

~~~text
Use fortnite_status and tell me whether Fortnite Porting is ready.
~~~

Then test real file browsing:

~~~text
Use fortnite_list_directory with an empty path and show me the root Fortnite folders.
~~~

Or let the agent search directly:

~~~text
Search the Fortnite virtual file system for files containing "Midas".
Inspect the most relevant packages and tell me what objects they contain.
~~~

If the client can see and call the fortnite_* tools, the MCP connection is working.

# MCP Tools

## Status and Asset Registry

| Tool | Purpose |
| --- | --- |
| fortnite_status | Check Fortnite/CUE4Parse readiness and Blender/Unreal plugin status |
| fortnite_list_asset_types | List registry-filterable Fortnite Porting asset types |
| fortnite_search_assets | Search the Fortnite Asset Registry by name, path, class, and optional type |
| fortnite_list_assets | Page through assets for a specific Fortnite Porting type |
| fortnite_get_asset | Load an Unreal object and inspect its metadata/export type |
| fortnite_get_asset_properties | Return the raw property view used by Fortnite Porting |
| fortnite_export_asset | Export an Asset Registry object to Blender, Unreal Engine, or the Assets Folder |

## Real Fortnite File Browser

| Tool | Purpose |
| --- | --- |
| fortnite_list_directory | Browse the real Fortnite virtual file-system tree one folder at a time |
| fortnite_search_files | Search real .uasset, .umap, and .ufont paths, optionally filtered by directory or extension |
| fortnite_get_file_info | Inspect a real file or directory entry and its VFS source |
| fortnite_list_package_objects | Open a package and enumerate its Unreal objects and detected export types |
| fortnite_export_file | Export a file found through the Files tree to Blender, Unreal Engine, or the Assets Folder |

The file-browser tools use Fortnite Porting's actual CUE4Parse provider, so an MCP agent can navigate the same Fortnite virtual file system that the application's **Files** tab uses.

# Export Targets

| Target | Requirement | Details |
| --- | --- | --- |
| **Blender** | Blender + Fortnite Porting Blender plugin running | Live import through the companion plugin |
| **Unreal Engine** | Unreal Engine + Fortnite Porting / UEFormat plugins running | Live import into Unreal Engine |
| **Assets Folder** | None | Offline export to Fortnite Porting's configured assets directory |

For MCP exports, use one of these target names:

~~~text
blender
unreal
assets_folder
~~~

If the Blender or Unreal companion plugin is not running, the corresponding MCP export tool returns an error instead of silently failing.

# Server Configuration

By default the MCP server binds only to:

~~~text
http://127.0.0.1:6010
~~~

Environment variables:

| Variable | Purpose |
| --- | --- |
| FORTNITE_PORTING_MCP_URL | Override the MCP server base URL |
| FORTNITE_PORTING_MCP_DISABLED=1 | Disable the MCP server |

Example:

~~~powershell
$env:FORTNITE_PORTING_MCP_URL="http://127.0.0.1:7000"
./FortnitePorting.exe
~~~

The MCP endpoint would then be:

~~~text
http://127.0.0.1:7000/mcp
~~~

> [!WARNING]
> The MCP server currently has no authentication layer. The default loopback binding keeps it local to your machine. Do not expose it to your LAN or the public internet unless you understand the security implications.

# Troubleshooting

### curl http://127.0.0.1:6010/health cannot connect

Make sure:

- FortnitePorting.exe is running.
- You are running the MCP-enabled build from this repository.
- FORTNITE_PORTING_MCP_DISABLED is not set to 1.
- Another application is not already using port 6010.

### Health works but fortniteReady is false

Fortnite Porting is still initializing Fortnite/CUE4Parse data. Finish the initial setup and wait for loading to complete.

### The AI client connects but cannot see the tools

Reconnect or restart the MCP client after adding the configuration. Make sure the URL ends in /mcp and not /health.

### Blender or Unreal export fails

Open Fortnite Porting's **Plugin** tab and make sure the appropriate companion plugin is installed and the target application/plugin is running.

Searching and file browsing do not require those companion plugins.

### File browsing says the Files tree is still loading

The application is still building its virtual file tree. Wait briefly and retry. fortnite_search_files can search the underlying provider even while the folder tree is still being prepared.

# Original Fortnite Porting

The original project is available here:

- [Fortnite Porting GitHub](https://github.com/h4lfheart/FortnitePorting)
- [Website](https://fortniteporting.app)
- [Discord](https://discord.gg/fortniteporting)
- [X / Twitter](https://twitter.com/FortnitePorting)
- [Support on Ko-fi](https://ko-fi.com/h4lfheart)

Community-maintained ports also exist for other platforms:

- [FortnitePortingMac](https://github.com/skythumbnails/FortnitePortingMac)
- [FortnitePorting-linux](https://github.com/fclivaz42/FortnitePorting-linux)

These are separate projects and this MCP fork currently targets Windows x64.

# License

Fortnite Porting is distributed under the [GNU General Public License v3.0](LICENSE).

Fortnite Porting is an independent project and is not affiliated with, endorsed by, or sponsored by Epic Games. Fortnite and related marks are trademarks of Epic Games. You are responsible for ensuring that your use of exported assets complies with applicable licenses and terms.

---

## Original Contributors

- [Chippy](https://github.com/Bmarquez1997) - Material system and export feature development.
- [Ghost](https://github.com/GhostScissors) - RADA and BINKA audio decoders and asset deserialization fixes.
- [Asval](https://github.com/4sval) - Inspiration for Fortnite Porting and a primary contributor to [CUE4Parse](https://github.com/FabianFG/CUE4Parse).
- [GMatrix](https://github.com/GMatrixGames) - AES key and mapping infrastructure through UEDB.
- [Marcel](https://github.com/Ka1serM) - Unreal Engine and UEFormat plugins, world export features, and project inspiration.
- [MountainFlash](https://github.com/MinshuG) - Inspiration for the project's early automation.
- [RedHaze](https://github.com/RedHaze) - Pose Asset processing and UEFormat pose export groundwork.

Thank you to everyone who has contributed to Fortnite Porting throughout its development.
