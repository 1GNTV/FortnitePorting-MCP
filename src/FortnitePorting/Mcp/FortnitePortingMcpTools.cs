using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using FortnitePorting.Application;
using FortnitePorting.Extensions;
using FortnitePorting.Models.Assets.Loading;
using FortnitePorting.Services;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace FortnitePorting.Mcp;

[McpServerToolType]
public sealed class FortnitePortingMcpTools
{
    [McpServerTool(Name = "fortnite_status")]
    [Description("Returns Fortnite Porting, asset registry, MCP endpoint, and live export plugin status.")]
    public static async Task<string> FortniteStatus()
    {
        var blender = await AppServices.ExportClient.IsRunning(EExportServerType.Blender);
        var unreal = await AppServices.ExportClient.IsRunning(EExportServerType.Unreal);

        return Json(new
        {
            mcp = new
            {
                running = AppServices.Mcp.IsRunning,
                endpoint = AppServices.Mcp.Endpoint
            },
            fortnite = new
            {
                ready = AppServices.UEParse.FinishedLoading,
                loading = AppServices.UEParse.IsLoading,
                status = AppServices.UEParse.Status,
                registryAssets = AppServices.UEParse.AssetRegistry.Count
            },
            exportTargets = new
            {
                blender = new { pluginRunning = blender },
                unreal = new { pluginRunning = unreal },
                assetsFolder = new
                {
                    available = true,
                    path = AppServices.AppSettings.Application.AssetPath
                }
            }
        });
    }

    [McpServerTool(Name = "fortnite_list_asset_types")]
    [Description("Lists Fortnite asset categories that can be filtered through the asset registry.")]
    public static string FortniteListAssetTypes()
    {
        var types = AppServices.AssetLoading.Categories
            .SelectMany(category => category.Loaders)
            .Select(loader => new
            {
                type = loader.Type.ToString(),
                registrySearchable = loader.ClassNames.Length > 0,
                unrealClasses = loader.ClassNames
            })
            .OrderBy(item => item.type)
            .ToArray();

        return Json(types);
    }

    [McpServerTool(Name = "fortnite_search_assets")]
    [Description("Searches the loaded Fortnite asset registry by asset name, package path, object path, or Unreal class. Optionally filter by a Fortnite Porting asset type such as Outfit, Pickaxe, Emote, Prop, or Item.")]
    public static string FortniteSearchAssets(
        [Description("Text to search for, for example Midas, CID_694, Pickaxe, or a package path fragment.")] string query,
        [Description("Maximum number of results (1-100).")] int limit = 25,
        [Description("Optional Fortnite Porting asset type, for example Outfit, Pickaxe, Emote, Prop, or Item.")] string? type = null)
    {
        if (!TryEnsureReady(out var notReady))
            return notReady;

        limit = Math.Clamp(limit, 1, 100);

        AssetLoader? loader = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!TryResolveLoader(type, out loader, out var typeError))
                return typeError;

            if (loader!.ClassNames.Length == 0)
            {
                return Json(new
                {
                    error = $"Asset type '{loader.Type}' is not backed by Asset Registry class names.",
                    hint = "Use fortnite_get_asset when you already know the Unreal object path."
                });
            }
        }

        var normalized = query?.Trim() ?? string.Empty;
        var assets = AppServices.UEParse.AssetRegistry.AsEnumerable();

        if (loader is not null)
        {
            var classes = loader.ClassNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            assets = assets.Where(asset => classes.Contains(asset.AssetClass.Text));
        }

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            assets = assets.Where(asset =>
                Contains(asset.AssetName.Text, normalized) ||
                Contains(asset.PackageName.Text, normalized) ||
                Contains(asset.ObjectPath.ToString(), normalized) ||
                Contains(asset.AssetClass.Text, normalized));
        }

        var results = assets
            .Take(limit)
            .Select(asset => new
            {
                name = asset.AssetName.Text,
                assetClass = asset.AssetClass.Text,
                package = asset.PackageName.Text,
                objectPath = asset.ObjectPath.ToString()
            })
            .ToArray();

        return Json(new
        {
            query = normalized,
            type = loader?.Type.ToString(),
            count = results.Length,
            results
        });
    }

    [McpServerTool(Name = "fortnite_list_assets")]
    [Description("Lists assets for a Fortnite Porting type directly from the loaded Fortnite Asset Registry.")]
    public static string FortniteListAssets(
        [Description("Fortnite Porting asset type, for example Outfit, Backpack, Pickaxe, Emote, Prop, or Item.")] string type,
        [Description("Zero-based result offset.")] int offset = 0,
        [Description("Maximum number of results (1-100).")] int limit = 50)
    {
        if (!TryEnsureReady(out var notReady))
            return notReady;

        if (!TryResolveLoader(type, out var loader, out var typeError))
            return typeError;

        if (loader!.ClassNames.Length == 0)
        {
            return Json(new
            {
                error = $"Asset type '{loader.Type}' is not backed by Asset Registry class names."
            });
        }

        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 100);
        var classes = loader.ClassNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filtered = AppServices.UEParse.AssetRegistry
            .Where(asset => classes.Contains(asset.AssetClass.Text))
            .OrderBy(asset => asset.AssetName.Text, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = filtered
            .Skip(offset)
            .Take(limit)
            .Select(asset => new
            {
                name = asset.AssetName.Text,
                assetClass = asset.AssetClass.Text,
                package = asset.PackageName.Text,
                objectPath = asset.ObjectPath.ToString()
            })
            .ToArray();

        return Json(new
        {
            type = loader.Type.ToString(),
            total = filtered.Length,
            offset,
            count = results.Length,
            results
        });
    }

    [McpServerTool(Name = "fortnite_get_asset")]
    [Description("Loads a Fortnite Unreal object by path and returns its core metadata and the export type detected by Fortnite Porting.")]
    public static async Task<string> FortniteGetAsset(
        [Description("Unreal object path returned by fortnite_search_assets, or another valid Fortnite object path.")] string path)
    {
        if (!TryEnsureReady(out var notReady))
            return notReady;

        var asset = await AppServices.UEParse.Provider!.SafeLoadPackageObjectAsync(path);
        if (asset is null)
        {
            return Json(new
            {
                error = "Asset could not be loaded.",
                path
            });
        }

        var exportType = AppServices.Exporter.DetermineExportType(asset);

        return Json(new
        {
            name = asset.Name,
            path = asset.GetPathName(),
            unrealClass = asset.ExportType,
            outer = asset.Outer?.Name,
            exportType = exportType.ToString()
        });
    }

    [McpServerTool(Name = "fortnite_get_asset_properties")]
    [Description("Returns Fortnite Porting's raw JSON-style property view for the package containing an Unreal asset path. Large responses are truncated.")]
    public static async Task<string> FortniteGetAssetProperties(
        [Description("Valid Fortnite Unreal object path.")] string path,
        [Description("Maximum number of characters returned (1,000-200,000).")] int maxCharacters = 50000)
    {
        if (!TryEnsureReady(out var notReady))
            return notReady;

        var asset = await AppServices.UEParse.Provider!.SafeLoadPackageObjectAsync(path);
        if (asset is null)
            return Json(new { error = "Asset could not be loaded.", path });

        maxCharacters = Math.Clamp(maxCharacters, 1000, 200000);

        var packageObjects = await AppServices.UEParse.Provider.LoadAllObjectsAsync(
            AppServices.Exporter.FixPath(asset.GetPathName()));

        var json = JsonConvert.SerializeObject(packageObjects, Formatting.Indented);
        if (json.Length <= maxCharacters)
            return json;

        return json[..maxCharacters] +
               $"\n\n/* truncated by FortnitePorting-MCP: {json.Length - maxCharacters} characters omitted */";
    }

    [McpServerTool(Name = "fortnite_export_asset")]
    [Description("Exports a Fortnite Unreal asset through Fortnite Porting. Supported targets are blender, unreal, and assets_folder. Blender or Unreal must have the corresponding Fortnite Porting plugin running.")]
    public static async Task<string> FortniteExportAsset(
        [Description("Valid Fortnite Unreal object path.")] string path,
        [Description("Export target: blender, unreal, or assets_folder.")] string target = "blender")
    {
        if (!TryEnsureReady(out var notReady))
            return notReady;

        var targetKey = target.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
        var location = targetKey switch
        {
            "blender" => EExportLocation.Blender,
            "unreal" or "unreal_engine" or "ue" => EExportLocation.Unreal,
            "assets" or "folder" or "assets_folder" => EExportLocation.AssetsFolder,
            _ => (EExportLocation?) null
        };

        if (location is null)
        {
            return Json(new
            {
                error = $"Unsupported export target '{target}'.",
                supportedTargets = new[] { "blender", "unreal", "assets_folder" }
            });
        }

        var asset = await AppServices.UEParse.Provider!.SafeLoadPackageObjectAsync(path);
        if (asset is null)
            return Json(new { error = "Asset could not be loaded.", path });

        EExportServerType serverType = location.Value switch
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
                hint = "Install/start the corresponding Fortnite Porting plugin, then retry."
            });
        }

        var meta = AppServices.AppSettings.ExportSettings.CreateExportMeta(location.Value);
        var success = await AppServices.Exporter.Export(asset, meta);

        return Json(new
        {
            success,
            name = asset.Name,
            path = asset.GetPathName(),
            exportType = AppServices.Exporter.DetermineExportType(asset).ToString(),
            target = location.Value.ToString(),
            assetsFolder = location.Value is EExportLocation.AssetsFolder
                ? AppServices.AppSettings.Application.AssetPath
                : null
        });
    }

    private static bool TryEnsureReady(out string error)
    {
        if (AppServices.UEParse.FinishedLoading && AppServices.UEParse.Provider is not null)
        {
            error = string.Empty;
            return true;
        }

        error = Json(new
        {
            error = "Fortnite assets are not ready yet.",
            loading = AppServices.UEParse.IsLoading,
            status = AppServices.UEParse.Status,
            hint = "Finish Fortnite Porting setup and wait for its CUE4Parse loading stage to complete."
        });
        return false;
    }

    private static bool TryResolveLoader(string type, out AssetLoader? loader, out string error)
    {
        loader = null;
        error = string.Empty;

        if (!Enum.TryParse<EExportType>(type.Trim(), true, out var exportType))
        {
            error = Json(new
            {
                error = $"Unknown asset type '{type}'.",
                hint = "Call fortnite_list_asset_types to see valid type names."
            });
            return false;
        }

        loader = AppServices.AssetLoading.Categories
            .SelectMany(category => category.Loaders)
            .FirstOrDefault(candidate => candidate.Type == exportType);

        if (loader is not null)
            return true;

        error = Json(new
        {
            error = $"Asset type '{exportType}' does not have an AssetLoader.",
            hint = "Call fortnite_list_asset_types to see types supported by registry filtering."
        });
        return false;
    }

    private static bool Contains(string? source, string value) =>
        source?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;

    private static string Json(object value) =>
        JsonConvert.SerializeObject(value, Formatting.Indented);
}
