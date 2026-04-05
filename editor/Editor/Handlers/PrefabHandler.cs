// Editor/Handlers/PrefabHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// prefab tool: instantiate, get_structure, get_instances, break, update,
/// create, save_overrides, revert, get_overrides.
/// Ported from Ozmium OzmiumWriteHandlers.InstantiatePrefab, OzmiumAssetHandlers.GetPrefabStructure,
/// SceneToolHandlers.GetPrefabInstances, OzmiumEditorHandlers.BreakFromPrefab/UpdateFromPrefab.
/// New actions: create, save_overrides, revert, get_overrides.
/// </summary>
internal static class PrefabHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "instantiate"    => Instantiate( args ),
                "get_structure"  => GetStructure( args ),
                "get_instances"  => GetInstances( args ),
                "break"          => Break( args ),
                "update"         => Update( args ),
                "create"         => Create( args ),
                "save_overrides" => SaveOverrides( args ),
                "revert"         => Revert( args ),
                "get_overrides"  => GetOverrides( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: instantiate, get_structure, get_instances, break, update, create, save_overrides, revert, get_overrides" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── instantiate ─────────────────────────────────────────────────────
    // Ported from OzmiumWriteHandlers.InstantiatePrefab

    private static object Instantiate( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "instantiate" );

        var path = SceneHelpers.NormalizePath( HandlerBase.GetString( args, "path" ) );
        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "instantiate" );

        var posStr = HandlerBase.GetString( args, "position" );
        var rotStr = HandlerBase.GetString( args, "rotation" );

        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;
        var rotation = rotStr != null
            ? Rotation.From( HandlerBase.ParseVector3( rotStr ).x,
                             HandlerBase.ParseVector3( rotStr ).y,
                             HandlerBase.ParseVector3( rotStr ).z )
            : Rotation.Identity;

        var asset = AssetSystem.FindByPath( path );
        if ( asset == null )
            return HandlerBase.Error( $"Asset not found: '{path}'. Use asset_query.search with type 'prefab' to find valid paths.", "instantiate" );

        var prefabFile = ResourceLibrary.Get<PrefabFile>( path );
        if ( prefabFile == null )
            return HandlerBase.Error( $"Could not load prefab: '{path}'.", "instantiate" );

        var prefabScene = SceneUtility.GetPrefabScene( prefabFile );
        var go = prefabScene.Clone();
        go.WorldPosition = position;
        go.WorldRotation = rotation;

        return HandlerBase.Success( new
        {
            message = $"Instantiated '{path}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    // ── get_structure ────────────────────────────────────────────────────
    // Ported from OzmiumAssetHandlers.GetPrefabStructure

    private static object GetStructure( JsonElement args )
    {
        var path = SceneHelpers.NormalizePath( HandlerBase.GetString( args, "path" ) );
        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "get_structure" );

        var asset = AssetSystem.FindByPath( path );
        if ( asset == null )
            return HandlerBase.Error( $"Prefab not found: '{path}'.", "get_structure" );

        if ( !System.IO.File.Exists( asset.AbsolutePath ) )
            return HandlerBase.Error( $"Prefab file not on disk: '{path}'.", "get_structure" );

        var raw = System.IO.File.ReadAllText( asset.AbsolutePath );

        // Truncate if too long — raw JSON can be huge
        if ( raw.Length > HandlerBase.MaxResponseChars - 200 )
            raw = raw[..(HandlerBase.MaxResponseChars - 200)] + "\n... [truncated]";

        return HandlerBase.Text( $"Raw prefab JSON for '{path}':\n{raw}" );
    }

    // ── get_instances ────────────────────────────────────────────────────
    // Ported from SceneToolHandlers.GetPrefabInstances

    private static object GetInstances( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "get_instances" );

        var prefabPath = HandlerBase.GetString( args, "path" );
        var offset = HandlerBase.GetInt( args, "offset", 0 );
        var limit = HandlerBase.GetInt( args, "limit", 50 );

        // No path — return a breakdown of all prefab sources
        if ( string.IsNullOrEmpty( prefabPath ) )
        {
            var counts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
            foreach ( var go in SceneHelpers.WalkAll( scene ) )
            {
                if ( !go.IsPrefabInstance || go.PrefabInstanceSource == null ) continue;
                counts.TryGetValue( go.PrefabInstanceSource, out var c );
                counts[go.PrefabInstanceSource] = c + 1;
            }

            var breakdown = counts
                .OrderByDescending( kv => kv.Value )
                .Select( kv => (object)new { prefab = kv.Key, instances = kv.Value } )
                .ToList();

            return HandlerBase.Paginate( breakdown, offset, limit, x => x );
        }

        // Return instances of a specific prefab
        var matches = SceneHelpers.WalkAll( scene )
            .Where( go => go.IsPrefabInstance
                && go.PrefabInstanceSource != null
                && go.PrefabInstanceSource.IndexOf( prefabPath, StringComparison.OrdinalIgnoreCase ) >= 0 )
            .ToList();

        return HandlerBase.Paginate( matches, offset, limit, go => new
        {
            id = go.Id.ToString(),
            name = go.Name,
            enabled = go.Enabled,
            position = HandlerBase.V3( go.WorldPosition ),
            prefabSource = go.PrefabInstanceSource
        } );
    }

    // ── break ────────────────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.BreakFromPrefab

    private static object Break( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "break" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "break" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "break" );
        go.BreakFromPrefab();
        return HandlerBase.WriteConfirm( go, $"Broke '{go.Name}' from its prefab source." );
    }

    // ── update ────────────────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.UpdateFromPrefab

    private static object Update( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "update" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "update" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "update" );
        go.UpdateFromPrefab();
        return HandlerBase.WriteConfirm( go, $"Updated '{go.Name}' from its prefab source." );
    }

    // ── create ───────────────────────────────────────────────────────────
    // NEW action — Research API first
    // Query: mcp__api__search_members "PrefabFile" + "SaveToFile"
    // Query: mcp__api__search_members "SceneUtility" + "CreatePrefab"
    // Query: mcp__api__get_type "PrefabUtility" (editor namespace)
    // Fallback approach: serialize GO to JSON, write as .prefab file

    private static object Create( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "create" );

        var id = HandlerBase.GetString( args, "id" );
        var savePath = HandlerBase.GetString( args, "save_path" );

        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "create" );
        if ( string.IsNullOrEmpty( savePath ) )
            return HandlerBase.Error( "Missing required 'save_path' parameter.", "create" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "create" );

        // Serialize the GameObject to a prefab JSON file.
        // s&box prefabs are .prefab files containing serialized GameObject JSON.
        // Use SceneUtility to serialize the object, then write to disk.
        var json = go.Serialize().ToString();
        var fullPath = savePath;

        // Ensure the path ends with .prefab
        if ( !fullPath.EndsWith( ".prefab", StringComparison.OrdinalIgnoreCase ) )
            fullPath += ".prefab";

        // Write to project directory via Editor.FileSystem
        // Research note: if Editor.FileSystem.Root is not available,
        // fall back to AssetSystem path resolution
        try
        {
            // Try writing via the asset system's base path
            var projectDir = System.IO.Path.GetDirectoryName(
                Editor.EditorUtility.Projects.Active?.GetAssemblyPath() ?? "" );

            // Fallback: use the asset system to find base path
            // Write relative to project root
            var dir = System.IO.Path.GetDirectoryName( fullPath );
            if ( !string.IsNullOrEmpty( dir ) && !System.IO.Directory.Exists( dir ) )
                System.IO.Directory.CreateDirectory( dir );

            System.IO.File.WriteAllText( fullPath, json );

            return HandlerBase.Success( new
            {
                message = $"Created prefab from '{go.Name}'.",
                path = fullPath,
                sourceId = go.Id.ToString()
            } );
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( $"Failed to write prefab file: {ex.Message}", "create",
                "Ensure the save_path is a valid relative path under the project (e.g. 'prefabs/my_thing.prefab')." );
        }
    }

    // ── save_overrides ────────────────────────────────────────────────────
    // NEW action — Research API first
    // Query: mcp__api__search_members "GameObject" + "SaveToPrefab"
    // Query: mcp__api__search_members "PrefabInstance" + "Apply"
    // The approach: serialize the instance back to the prefab source file

    private static object SaveOverrides( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "save_overrides" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "save_overrides" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "save_overrides" );

        if ( !go.IsPrefabInstance || string.IsNullOrEmpty( go.PrefabInstanceSource ) )
            return HandlerBase.Error( $"'{go.Name}' is not a prefab instance.", "save_overrides" );

        // Find the source prefab path and overwrite it with the instance's serialized state
        var sourcePath = go.PrefabInstanceSource;
        var asset = AssetSystem.FindByPath( sourcePath );
        if ( asset == null )
            return HandlerBase.Error( $"Source prefab not found: '{sourcePath}'.", "save_overrides" );

        try
        {
            var json = go.Serialize().ToString();
            System.IO.File.WriteAllText( asset.AbsolutePath, json );
            asset.Compile( true );

            return HandlerBase.Success( new
            {
                message = $"Saved overrides from '{go.Name}' back to '{sourcePath}'.",
                path = sourcePath,
                id = go.Id.ToString()
            } );
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( $"Failed to save overrides: {ex.Message}", "save_overrides" );
        }
    }

    // ── revert ────────────────────────────────────────────────────────────
    // NEW action — uses UpdateFromPrefab (same as update)
    // UpdateFromPrefab discards instance changes and matches the source

    private static object Revert( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "revert" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "revert" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "revert" );

        if ( !go.IsPrefabInstance )
            return HandlerBase.Error( $"'{go.Name}' is not a prefab instance.", "revert" );

        go.UpdateFromPrefab();
        return HandlerBase.WriteConfirm( go, $"Reverted '{go.Name}' to match its source prefab." );
    }

    // ── get_overrides ─────────────────────────────────────────────────────
    // NEW action — Research API first
    // Query: mcp__api__search_members "GameObject" + "GetPrefabOverrides"
    // Query: mcp__api__search_members "PrefabInstance" + "Override"
    // Fallback: compare serialized instance JSON to source prefab JSON

    private static object GetOverrides( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "get_overrides" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "get_overrides" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "get_overrides" );

        if ( !go.IsPrefabInstance || string.IsNullOrEmpty( go.PrefabInstanceSource ) )
            return HandlerBase.Error( $"'{go.Name}' is not a prefab instance.", "get_overrides" );

        var sourcePath = go.PrefabInstanceSource;
        var asset = AssetSystem.FindByPath( sourcePath );
        if ( asset == null )
            return HandlerBase.Error( $"Source prefab not found: '{sourcePath}'.", "get_overrides" );

        try
        {
            // Read the source prefab JSON
            if ( !System.IO.File.Exists( asset.AbsolutePath ) )
                return HandlerBase.Error( $"Source prefab file not on disk: '{sourcePath}'.", "get_overrides" );

            var sourceJson = System.IO.File.ReadAllText( asset.AbsolutePath );
            var instanceJson = go.Serialize().ToString();

            // Parse both and compare top-level properties
            using var sourceDoc = JsonDocument.Parse( sourceJson );
            using var instanceDoc = JsonDocument.Parse( instanceJson );

            var overrides = new List<object>();

            foreach ( var prop in instanceDoc.RootElement.EnumerateObject() )
            {
                if ( sourceDoc.RootElement.TryGetProperty( prop.Name, out var sourceProp ) )
                {
                    if ( prop.Value.ToString() != sourceProp.ToString() )
                    {
                        overrides.Add( new
                        {
                            property = prop.Name,
                            instanceValue = prop.Value.ToString(),
                            sourceValue = sourceProp.ToString()
                        } );
                    }
                }
                else
                {
                    overrides.Add( new
                    {
                        property = prop.Name,
                        instanceValue = prop.Value.ToString(),
                        sourceValue = (string)null
                    } );
                }
            }

            return HandlerBase.Success( new
            {
                id = go.Id.ToString(),
                name = go.Name,
                prefabSource = sourcePath,
                overrideCount = overrides.Count,
                overrides
            } );
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( $"Failed to compare overrides: {ex.Message}", "get_overrides" );
        }
    }
}
