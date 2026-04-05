// Editor/Handlers/CloudHandler.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// cloud tool (async): search, get_package, mount.
/// Ported from CloudAssetHandlers.cs (213 lines).
/// Uses HandleAsync pattern — dispatched BEFORE GameTask.MainThread().
/// </summary>
internal static class CloudHandler
{
    internal static async Task<object> HandleAsync( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "search"      => await Search( args ),
                "get_package" => await GetPackage( args ),
                "mount"       => await Mount( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: search, get_package, mount" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── search ────────────────────────────────────────────────────────
    // Ported from CloudAssetHandlers.SearchCloudAssets

    private static async Task<object> Search( JsonElement args )
    {
        var query = HandlerBase.GetString( args, "query" );
        if ( string.IsNullOrEmpty( query ) )
            return HandlerBase.Error( "Missing required 'query' parameter.", "search" );

        var type = HandlerBase.GetString( args, "type" );
        var maxResults = HandlerBase.GetInt( args, "max_results", 10 );
        maxResults = Math.Clamp( maxResults, 1, 50 );

        var fullQuery = query;
        if ( !string.IsNullOrEmpty( type ) )
            fullQuery = $"type:{type} {query}";

        var findResult = await Package.FindAsync( fullQuery, take: maxResults, token: CancellationToken.None );

        if ( findResult?.Packages == null || findResult.Packages.Length == 0 )
            return HandlerBase.Text( $"No results for '{fullQuery}'." );

        var items = findResult.Packages.Select( p => new
        {
            ident = p.FullIdent,
            title = p.Title,
            summary = p.Summary,
            type = p.TypeName,
            thumb = p.Thumb
        } ).ToList();

        return HandlerBase.Success( new
        {
            query = fullQuery,
            count = items.Count,
            total_count = findResult.TotalCount,
            results = items
        } );
    }

    // ── get_package ───────────────────────────────────────────────────
    // Ported from CloudAssetHandlers.GetCloudPackage

    private static async Task<object> GetPackage( JsonElement args )
    {
        var ident = HandlerBase.GetString( args, "ident" );
        if ( string.IsNullOrEmpty( ident ) )
            return HandlerBase.Error( "Missing required 'ident' parameter.", "get_package" );

        var pkg = await Package.FetchAsync( ident, false );
        if ( pkg == null )
            return HandlerBase.Error( $"Package '{ident}' not found.", "get_package" );

        return HandlerBase.Success( new
        {
            ident = pkg.FullIdent,
            title = pkg.Title,
            summary = pkg.Summary,
            description = pkg.Description?.Length > 500
                ? pkg.Description.Substring( 0, 500 ) + "..."
                : pkg.Description,
            type = pkg.TypeName,
            org = pkg.Org?.Ident,
            thumb = pkg.Thumb,
            primary_asset = pkg.PrimaryAsset,
            tags = pkg.Tags,
            file_size = pkg.FileSize
        } );
    }

    // ── mount ─────────────────────────────────────────────────────────
    // Ported from CloudAssetHandlers.MountCloudAsset

    private static async Task<object> Mount( JsonElement args )
    {
        var ident = HandlerBase.GetString( args, "ident" );
        if ( string.IsNullOrEmpty( ident ) )
            return HandlerBase.Error( "Missing required 'ident' parameter.", "mount" );

        var pkg = await Package.FetchAsync( ident, false );
        if ( pkg == null )
            return HandlerBase.Error( $"Package '{ident}' not found.", "mount" );

        // Mount the package — makes assets available to the editor
        await pkg.MountAsync();

        // Add to .sbproj PackageReferences for persistence across restarts
        // This requires main thread for file I/O, use GameTask
        bool added = false;
        await GameTask.MainThread();
        added = AddPackageReference( ident );

        var status = added
            ? "Mounted and added to .sbproj PackageReferences."
            : "Mounted (already in PackageReferences).";

        return HandlerBase.Text( $"{status} Package: '{ident}'" );
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static bool AddPackageReference( string ident )
    {
        try
        {
            var root = FindProjectRoot();
            if ( root == null ) return false;

            var sbprojFiles = Directory.GetFiles( root, "*.sbproj" );
            if ( sbprojFiles.Length == 0 ) return false;

            var path = sbprojFiles[0];
            var content = File.ReadAllText( path );
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>( content );

            var refs = new List<string>();
            if ( doc.TryGetValue( "PackageReferences", out var refsEl ) && refsEl.ValueKind == JsonValueKind.Array )
            {
                foreach ( var item in refsEl.EnumerateArray() )
                    refs.Add( item.GetString() );
            }

            if ( refs.Contains( ident ) ) return false;

            refs.Add( ident );
            doc["PackageReferences"] = JsonSerializer.SerializeToElement( refs );

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText( path, JsonSerializer.Serialize( doc, options ) );
            return true;
        }
        catch { return false; }
    }

    private static string FindProjectRoot()
    {
        try
        {
            var libPath = Editor.FileSystem.Libraries.GetFullPath( "" );
            return Path.GetDirectoryName( libPath );
        }
        catch
        {
            return null;
        }
    }
}
