using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.VirtualFileSystem;
using CUE4Parse.Utils;
using FortnitePorting.Application;
using FortnitePorting.CUE4Parse.Models.Unreal;
using FortnitePorting.CUE4Parse.Models.Unreal.VirtualTexture;
using FortnitePorting.Exporting.Models.Files;
using FortnitePorting.Extensions;
using FortnitePorting.Models.Files;
using FortnitePorting.Services;
using ModelContextProtocol.Server;

namespace FortnitePorting.Mcp;

public sealed partial class FortnitePortingMcpTools
{
    [McpServerTool(Name = "fortnite_list_directory")]
    [Description("Lists the real Fortnite virtual file-system directory used by Fortnite Porting. Use this to browse folders exactly like the Files tab.")]
    public static string FortniteListDirectory(
        [Description("Directory path such as FortniteGame/Content/Athena. Use an empty string for the root.")] string path = "",
        [Description("Zero-based child offset.")] int offset = 0,
        [Description("Maximum number of direct children to return (1-250).")] int limit = 100)
    {
        if (!TryEnsureReady(out var notReady))
            return notReady;

        if (AppServices.Files.IsLoading)
        {
            return Json(new
            {
                error = "Fortnite Porting is still building its Files tree.",
                loadedFiles = AppServices.Files.LoadedFiles,
                totalFiles = AppServices.Files.TotalFiles,
                hint = "Retry in a moment. fortnite_search_files can already search the provider while the tree is building."
            });
        }

        var normalized = NormalizeFilePath(path);
        var node = ResolveFileNode(normalized);
        if (node is null)
            return Json(new { error = "Directory was not found.", path = normalized });

        if (node.Type.ToString().Equals("File", StringComparison.OrdinalIgnoreCase))
            return Json(new { error = "The supplied path is a file, not a directory.", path = normalized });

        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 250);

        var children = node.Children.Values
            .OrderByDescending(child => child.Type.ToString().Equals("Folder", StringComparison.OrdinalIgnoreCase))
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = children
            .Skip(offset)
            .Take(limit)
            .Select(child => new
            {
                name = child.Name,
                path = child.Path,
                type = child.Type.ToString(),
                fileChildren = child.FileChildCount,
                folderChildren = child.FolderChildCount,
                vfs = child.VfsNames.OrderBy(name => name).ToArray()
            })
            .ToArray();

        return Json(new
        {
            path = normalized,
            totalChildren = children.Length,
            offset,
            count = results.Length,
            results
        });
    }

    [McpServerTool(Name = "fortnite_search_files")]
    [Description("Searches the real Fortnite virtual file system by file name or path. Supports optional directory and extension filters.")]
    public static string FortniteSearchFiles(
        [Description("File name or path fragment to search for.")] string query,
        [Description("Optional directory prefix such as FortniteGame/Content/Athena.")] string? path = null,
        [Description("Optional comma-separated extensions, for example .uasset,.umap or uasset,umap.")] string? extensions = null,
        [Description("Maximum number of results (1-250).")] int limit = 100)
    {
        if (!TryEnsureReady(out var notReady))
            return notReady;

        limit = Math.Clamp(limit, 1, 250);
        var normalizedQuery = query?.Trim() ?? string.Empty;
        var normalizedRoot = NormalizeFilePath(path ?? string.Empty);
        var extensionSet = ParseExtensions(extensions);

        var results = AppServices.UEParse.Provider!.Files.Values
            .Where(file => string.IsNullOrEmpty(normalizedRoot) || IsUnderPath(file.Path, normalizedRoot))
            .Where(file => extensionSet.Count == 0 || extensionSet.Contains(Path.GetExtension(file.Path)))
            .Where(file => string.IsNullOrEmpty(normalizedQuery) ||
                           file.Path.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                           file.Path.SubstringAfterLast("/").Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(file => new
            {
                name = file.Path.SubstringAfterLast("/"),
                path = file.Path,
                extension = Path.GetExtension(file.Path),
                vfs = file is VfsEntry vfsEntry ? vfsEntry.Vfs.Name : string.Empty
            })
            .ToArray();

        return Json(new
        {
            query = normalizedQuery,
            root = normalizedRoot,
            extensions = extensionSet.OrderBy(value => value).ToArray(),
            count = results.Length,
            results
        });
    }

    [McpServerTool(Name = "fortnite_get_file_info")]
    [Description("Returns metadata for a file or directory in Fortnite Porting's real Files tree.")]
    public static string FortniteGetFileInfo(
        [Description("File or directory path from fortnite_list_directory or fortnite_search_files.")] string path)
    {
        if (!TryEnsureReady(out var notReady))
            return notReady;

        var normalized = NormalizeFilePath(path);

        if (!AppServices.Files.IsLoading && ResolveFileNode(normalized) is { } node)
        {
            return Json(new
            {
                name = node.Name,
                path = node.Path,
                type = node.Type.ToString(),
                fileChildren = node.FileChildCount,
                folderChildren = node.FolderChildCount,
                vfs = node.VfsNames.OrderBy(name => name).ToArray()
            });
        }

        var file = AppServices.UEParse.Provider!.Files.Values
            .FirstOrDefault(candidate => candidate.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase));

        if (file is null)
            return Json(new { error = "File or directory was not found.", path = normalized });

        return Json(new
        {
            name = file.Path.SubstringAfterLast("/"),
            path = file.Path,
            type = "File",
            extension = Path.GetExtension(file.Path),
            vfs = file is VfsEntry vfsEntry ? vfsEntry.Vfs.Name : string.Empty
        });
    }

    [McpServerTool(Name = "fortnite_list_package_objects")]
    [Description("Loads a .uasset/.umap/.ufont package and lists the Unreal objects it contains, including the export type Fortnite Porting detects.")]
    public static async Task<string> FortniteListPackageObjects(
        [Description("Fortnite virtual file path such as FortniteGame/Content/.../Asset.uasset.")] string path,
        [Description("Maximum number of package objects to return (1-500).")] int limit = 200)
    {
        if (!TryEnsureReady(out var notReady))
            return notReady;

        limit = Math.Clamp(limit, 1, 500);
        var normalized = NormalizeFilePath(path);

        try
        {
            var objects = await AppServices.UEParse.Provider!.LoadAllObjectsAsync(
                AppServices.Exporter.FixPath(normalized));

            var results = objects
                .Take(limit)
                .Select(asset => new
                {
                    name = asset.Name,
                    path = asset.GetPathName(),
                    unrealClass = asset.ExportType,
                    exportType = AppServices.Exporter.DetermineExportType(asset).ToString(),
                    outer = asset.Outer?.Name
                })
                .ToArray();

            return Json(new
            {
                file = normalized,
                totalObjects = objects.Count,
                count = results.Length,
                results
            });
        }
        catch (Exception exception)
        {
            return Json(new
            {
                error = "Failed to load package objects.",
                path = normalized,
                details = exception.Message
            });
        }
    }

    [McpServerTool(Name = "fortnite_export_file")]
    [Description("Exports a file selected from Fortnite Porting's virtual Files tree through the existing Fortnite Porting export pipeline. Targets: blender, unreal, assets_folder.")]
    public static async Task<string> FortniteExportFile(
        [Description("Fortnite virtual file path ending in .uasset, .umap, or .ufont.")] string path,
        [Description("Export target: blender, unreal, or assets_folder.")] string target = "blender")
    {
        if (!TryEnsureReady(out var notReady))
            return notReady;

        var normalized = NormalizeFilePath(path);
        var location = ParseFileExportLocation(target);
        if (location is null)
        {
            return Json(new
            {
                error = $"Unsupported export target '{target}'.",
                supportedTargets = new[] { "blender", "unreal", "assets_folder" }
            });
        }

        var serverType = location.Value switch
        {
            EExportLocation.Blender => EExportServerType.Blender,
            EExportLocation.Unreal => EExportServerType.Unreal,
            _ => EExportServerType.None
        };

        if (serverType is not EExportServerType.None &&
            !await AppServices.ExportClient.IsRunning(serverType))
        {
            return Json(new
            {
                error = $"{serverType} export plugin is not running.",
                target = location.Value.ToString(),
                hint = "Start the corresponding Fortnite Porting companion plugin and retry."
            });
        }

        try
        {
            var asset = await LoadAssetFromFilePath(normalized);
            if (asset is null)
                return Json(new { error = "No exportable Unreal object could be loaded from this file.", path = normalized });

            asset = TransformFileAsset(asset);
            var exportType = AppServices.Exporter.DetermineExportType(asset);
            if (exportType is EExportType.None)
            {
                return Json(new
                {
                    error = "Fortnite Porting does not have an exporter for this Unreal object type.",
                    path = normalized,
                    unrealClass = asset.ExportType
                });
            }

            var entry = new ExportFileEntry
            {
                Object = asset,
                Type = exportType
            };

            var meta = AppServices.AppSettings.ExportSettings.CreateExportMeta(location.Value);
            meta.WorldFlags = EWorldFlags.Actors | EWorldFlags.Landscape | EWorldFlags.WorldPartitionGrids | EWorldFlags.HLODs;
            if (meta.Settings.ImportInstancedFoliage)
                meta.WorldFlags |= EWorldFlags.InstancedFoliage;

            var success = await AppServices.Exporter.Export(new[] { entry }, meta);

            return Json(new
            {
                success,
                file = normalized,
                objectName = asset.Name,
                objectPath = asset.GetPathName(),
                unrealClass = asset.ExportType,
                exportType = exportType.ToString(),
                target = location.Value.ToString(),
                assetsFolder = location.Value is EExportLocation.AssetsFolder
                    ? AppServices.AppSettings.Application.AssetPath
                    : null
            });
        }
        catch (Exception exception)
        {
            return Json(new
            {
                error = "File export failed.",
                path = normalized,
                details = exception.Message
            });
        }
    }

    private static FileNode? ResolveFileNode(string path)
    {
        var node = AppServices.Files.RootFileNode;
        if (string.IsNullOrEmpty(path))
            return node;

        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node.TryGetChild(part, out var exact))
            {
                node = exact;
                continue;
            }

            var insensitive = node.Children
                .FirstOrDefault(pair => pair.Key.Equals(part, StringComparison.OrdinalIgnoreCase))
                .Value;

            if (insensitive is null)
                return null;

            node = insensitive;
        }

        return node;
    }

    private static string NormalizeFilePath(string path) =>
        (path ?? string.Empty).Trim().Replace('\\', '/').Trim('/');

    private static bool IsUnderPath(string candidate, string root)
    {
        var normalizedCandidate = NormalizeFilePath(candidate);
        return normalizedCandidate.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> ParseExtensions(string? extensions)
    {
        if (string.IsNullOrWhiteSpace(extensions))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return extensions
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.StartsWith('.') ? value : "." + value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static EExportLocation? ParseFileExportLocation(string target)
    {
        var key = target.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
        return key switch
        {
            "blender" => EExportLocation.Blender,
            "unreal" or "unreal_engine" or "ue" => EExportLocation.Unreal,
            "assets" or "folder" or "assets_folder" => EExportLocation.AssetsFolder,
            _ => null
        };
    }

    private static async Task<UObject?> LoadAssetFromFilePath(string path)
    {
        var basePath = AppServices.Exporter.FixPath(path);

        if (path.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
        {
            var world = await AppServices.UEParse.Provider!.SafeLoadPackageObjectAsync(basePath);
            if (world is UWorld)
                return world;

            var package = await AppServices.UEParse.Provider.LoadPackageAsync(basePath);
            return package.GetExports().OfType<UWorld>().FirstOrDefault();
        }

        var asset = await AppServices.UEParse.Provider!.SafeLoadPackageObjectAsync(basePath);
        asset ??= await AppServices.UEParse.Provider.SafeLoadPackageObjectAsync(
            $"{basePath}.{basePath.SubstringAfterLast("/")}_C");

        return asset;
    }

    private static UObject TransformFileAsset(UObject asset) => asset switch
    {
        UVirtualTextureBuilder builder => builder.Texture.Load<UVirtualTexture2D>(),
        UPaperSprite sprite => sprite.BakedSourceTexture.Load<UTexture2D>(),
        _ => asset
    };
}
