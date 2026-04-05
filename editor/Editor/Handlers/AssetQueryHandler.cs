// Editor/Handlers/AssetQueryHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// asset_query tool: browse, search, open, get_dependencies, get_model_info,
/// get_material_properties, get_mesh_info, get_bounds, get_unsaved,
/// get_status, get_json, get_references.
/// All actions are read-only.
/// Ported from Ozmium OzmiumAssetHandlers, UtilityToolHandlers, OzmiumEditorHandlers.
/// New actions: get_status, get_json, get_references.
/// </summary>
internal static class AssetQueryHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "browse"                  => Browse( args ),
                "search"                  => Search( args ),
                "open"                    => Open( args ),
                "get_dependencies"        => GetDependencies( args ),
                "get_model_info"          => GetModelInfo( args ),
                "get_material_properties" => GetMaterialProperties( args ),
                "get_mesh_info"           => GetMeshInfo( args ),
                "get_bounds"              => GetBounds( args ),
                "get_unsaved"             => GetUnsaved(),
                "get_status"              => GetStatus( args ),
                "get_json"                => GetJson( args ),
                "get_references"          => GetReferences( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: browse, search, open, get_dependencies, get_model_info, get_material_properties, get_mesh_info, get_bounds, get_unsaved, get_status, get_json, get_references" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── browse ────────────────────────────────────────────────────────────
    // Ported from OzmiumAssetHandlers.BrowseAssets

    private static object Browse( JsonElement args )
    {
        var directory = HandlerBase.GetString( args, "directory" );
        var offset = HandlerBase.GetInt( args, "offset", 0 );
        var limit = HandlerBase.GetInt( args, "limit", 50 );

        var all = new List<Asset>();
        foreach ( var asset in AssetSystem.All )
        {
            if ( !string.IsNullOrEmpty( directory ) )
            {
                var aPath = asset.Path ?? "";
                if ( !aPath.StartsWith( directory, StringComparison.OrdinalIgnoreCase ) )
                    continue;
            }
            all.Add( asset );
        }

        return HandlerBase.Paginate( all, offset, limit, asset => new
        {
            path = asset.Path ?? "",
            relativePath = asset.RelativePath ?? asset.Path ?? "",
            name = asset.Name ?? "",
            type = asset.AssetType?.FriendlyName ?? asset.AssetType?.FileExtension ?? "",
            extension = asset.AssetType?.FileExtension ?? ""
        } );
    }

    // ── search ────────────────────────────────────────────────────────────
    // Ported from OzmiumAssetHandlers.SearchAssets

    private static object Search( JsonElement args )
    {
        var query = HandlerBase.GetString( args, "query" );
        var typeFilter = HandlerBase.GetString( args, "type" );
        var format = HandlerBase.GetString( args, "format", "concise" );
        var offset = HandlerBase.GetInt( args, "offset", 0 );
        var limit = HandlerBase.GetInt( args, "limit", 50 );

        if ( string.IsNullOrEmpty( query ) )
            return HandlerBase.Error( "Missing required 'query' parameter.", "search" );

        var matches = new List<Asset>();
        foreach ( var asset in AssetSystem.All )
        {
            var aName = asset.Name ?? "";
            var aPath = asset.Path ?? "";
            var ext = asset.AssetType?.FileExtension ?? "";
            var friendly = asset.AssetType?.FriendlyName ?? ext;

            bool nameMatch = aName.IndexOf( query, StringComparison.OrdinalIgnoreCase ) >= 0
                          || aPath.IndexOf( query, StringComparison.OrdinalIgnoreCase ) >= 0;
            if ( !nameMatch ) continue;

            if ( !string.IsNullOrEmpty( typeFilter ) )
            {
                bool typeMatch = ext.IndexOf( typeFilter, StringComparison.OrdinalIgnoreCase ) >= 0
                              || friendly.IndexOf( typeFilter, StringComparison.OrdinalIgnoreCase ) >= 0;
                if ( !typeMatch ) continue;
            }

            matches.Add( asset );
        }

        if ( format == "detailed" )
        {
            return HandlerBase.Paginate( matches, offset, limit, asset => new
            {
                path = asset.Path ?? "",
                relativePath = asset.RelativePath ?? asset.Path ?? "",
                name = asset.Name ?? "",
                type = asset.AssetType?.FriendlyName ?? "",
                extension = asset.AssetType?.FileExtension ?? "",
                absolutePath = asset.AbsolutePath ?? ""
            } );
        }

        return HandlerBase.Paginate( matches, offset, limit, asset => new
        {
            path = asset.Path ?? "",
            name = asset.Name ?? "",
            type = asset.AssetType?.FriendlyName ?? asset.AssetType?.FileExtension ?? ""
        } );
    }

    // ── open ──────────────────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.OpenAsset

    private static object Open( JsonElement args )
    {
        var path = SceneHelpers.NormalizePath( HandlerBase.GetString( args, "path" ) );
        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "open" );

        var asset = AssetSystem.FindByPath( path );
        if ( asset == null )
            return HandlerBase.Error( $"Asset not found: '{path}'.", "open" );

        asset.OpenInEditor();
        return HandlerBase.Confirm( $"Opened '{path}' in editor." );
    }

    // ── get_dependencies ──────────────────────────────────────────────────
    // Ported from UtilityToolHandlers.GetAssetDependencies

    private static object GetDependencies( JsonElement args )
    {
        var path = SceneHelpers.NormalizePath( HandlerBase.GetString( args, "path" ) );
        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "get_dependencies" );

        var asset = AssetSystem.FindByPath( path );
        if ( asset == null )
            return HandlerBase.Error( $"Asset not found: '{path}'.", "get_dependencies" );

        var deps = new List<string>();
        var visited = new HashSet<string>();
        CollectDependencies( asset, deps, visited );

        return HandlerBase.Success( new
        {
            asset = path,
            dependencyCount = deps.Count,
            dependencies = deps
        } );
    }

    private static void CollectDependencies( Asset asset, List<string> result, HashSet<string> visited )
    {
        if ( !visited.Add( asset.Path ) ) return;
        try
        {
            foreach ( var dep in asset.GetReferences( false ) )
            {
                var depPath = dep?.Path ?? "?";
                result.Add( depPath );
                var depAsset = AssetSystem.FindByPath( depPath );
                if ( depAsset != null )
                    CollectDependencies( depAsset, result, visited );
            }
        }
        catch { }
    }

    // ── get_model_info ────────────────────────────────────────────────────
    // Ported from OzmiumAssetHandlers.GetModelInfo

    private static object GetModelInfo( JsonElement args )
    {
        var path = SceneHelpers.NormalizePath( HandlerBase.GetString( args, "path" ) );
        var format = HandlerBase.GetString( args, "format", "concise" );

        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "get_model_info" );

        var model = Model.Load( path );
        if ( model == null )
            return HandlerBase.Error( $"Model not found: '{path}'.", "get_model_info" );

        var attachments = new List<object>();
        try
        {
            var attObj = model.Attachments;
            if ( attObj != null )
            {
                var countProp = attObj.GetType().GetProperty( "Count" )
                             ?? attObj.GetType().GetProperty( "Length" );
                int count = countProp != null ? (int)countProp.GetValue( attObj ) : 0;
                var indexer = attObj.GetType().GetProperty( "Item" );
                for ( int i = 0; i < count; i++ )
                {
                    try
                    {
                        var att = indexer?.GetValue( attObj, new object[] { i } );
                        var attName = att?.GetType().GetProperty( "Name" )?.GetValue( att )?.ToString() ?? $"att_{i}";
                        attachments.Add( new { name = attName, index = i } );
                    }
                    catch { attachments.Add( new { index = i } ); }
                }
            }
        }
        catch { }

        return HandlerBase.Success( new
        {
            path,
            boneCount = model.BoneCount,
            attachmentCount = attachments.Count,
            attachments = format == "detailed" ? attachments : null,
            bounds = new { mins = HandlerBase.V3( model.Bounds.Mins ), maxs = HandlerBase.V3( model.Bounds.Maxs ) }
        } );
    }

    // ── get_material_properties ───────────────────────────────────────────
    // Ported from OzmiumAssetHandlers.GetMaterialProperties

    private static object GetMaterialProperties( JsonElement args )
    {
        var path = SceneHelpers.NormalizePath( HandlerBase.GetString( args, "path" ) );
        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "get_material_properties" );

        var mat = Material.Load( path );
        if ( mat == null )
            return HandlerBase.Error( $"Material not found: '{path}'.", "get_material_properties" );

        return HandlerBase.Success( new
        {
            path,
            name = mat.Name,
            shader = mat.ShaderName
        } );
    }

    // ── get_mesh_info ─────────────────────────────────────────────────────
    // Partially ported from MeshEditHandlers.GetMeshInfo
    // Uses reflection to read EditorMeshComponent's PolygonMesh data

    private static object GetMeshInfo( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "get_mesh_info" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "get_mesh_info" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "get_mesh_info" );

        // Look for EditorMeshComponent which has a PolygonMesh property
        var meshComp = go.Components.GetAll()
            .FirstOrDefault( c => c.GetType().Name.Contains( "EditorMeshComponent" )
                               || c.GetType().Name.Contains( "PolygonMesh" ) );

        if ( meshComp == null )
        {
            // Fallback: check for ModelRenderer with a model
            var modelRenderer = go.Components.GetAll()
                .FirstOrDefault( c => c.GetType().Name.Contains( "ModelRenderer" ) );
            if ( modelRenderer != null )
            {
                var modelProp = modelRenderer.GetType().GetProperty( "Model" );
                var model = modelProp?.GetValue( modelRenderer ) as Model;
                if ( model != null && !model.IsError )
                {
                    return HandlerBase.Success( new
                    {
                        id = go.Id.ToString(),
                        name = go.Name,
                        type = "ModelRenderer",
                        boneCount = model.BoneCount,
                        bounds = new { mins = HandlerBase.V3( model.Bounds.Mins ), maxs = HandlerBase.V3( model.Bounds.Maxs ) }
                    } );
                }
            }

            return HandlerBase.Error( $"No mesh component found on '{go.Name}'.", "get_mesh_info" );
        }

        // Try to read vertex/face counts from PolygonMesh via reflection
        var meshProp = meshComp.GetType().GetProperty( "PolygonMesh" )
                    ?? meshComp.GetType().GetProperty( "Mesh" );
        if ( meshProp == null )
            return HandlerBase.Success( new
            {
                id = go.Id.ToString(),
                name = go.Name,
                type = meshComp.GetType().Name,
                note = "PolygonMesh property not found — mesh details unavailable."
            } );

        var mesh = meshProp.GetValue( meshComp );
        if ( mesh == null )
            return HandlerBase.Success( new
            {
                id = go.Id.ToString(),
                name = go.Name,
                type = meshComp.GetType().Name,
                note = "Mesh is null."
            } );

        // Read vertex and face counts via reflection
        var vertexCountProp = mesh.GetType().GetProperty( "VertexCount" )
                           ?? mesh.GetType().GetProperty( "Vertices" );
        var faceCountProp = mesh.GetType().GetProperty( "FaceCount" )
                         ?? mesh.GetType().GetProperty( "Faces" );

        int? vertexCount = null;
        int? faceCount = null;

        if ( vertexCountProp != null )
        {
            var val = vertexCountProp.GetValue( mesh );
            if ( val is int vc ) vertexCount = vc;
            else if ( val is System.Collections.ICollection coll ) vertexCount = coll.Count;
        }
        if ( faceCountProp != null )
        {
            var val = faceCountProp.GetValue( mesh );
            if ( val is int fc ) faceCount = fc;
            else if ( val is System.Collections.ICollection coll ) faceCount = coll.Count;
        }

        return HandlerBase.Success( new
        {
            id = go.Id.ToString(),
            name = go.Name,
            type = meshComp.GetType().Name,
            vertexCount,
            faceCount
        } );
    }

    // ── get_bounds ────────────────────────────────────────────────────────
    // Ported from UtilityToolHandlers.GetObjectBounds

    private static object GetBounds( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "get_bounds" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "get_bounds" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "get_bounds" );
        var bounds = SceneHelpers.GetGameObjectBounds( go );

        return HandlerBase.Success( new
        {
            id = go.Id.ToString(),
            name = go.Name,
            mins = HandlerBase.V3( bounds.Mins ),
            maxs = HandlerBase.V3( bounds.Maxs ),
            center = HandlerBase.V3( bounds.Center ),
            size = HandlerBase.V3( bounds.Size )
        } );
    }

    // ── get_unsaved ───────────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.GetSceneUnsaved (extended to all sessions)

    private static object GetUnsaved()
    {
        var results = new List<object>();
        try
        {
            foreach ( var s in SceneEditorSession.All )
            {
                if ( s == null ) continue;
                if ( s.HasUnsavedChanges )
                {
                    results.Add( new
                    {
                        name = s.Scene?.Name ?? "(unnamed)",
                        isActive = s == SceneEditorSession.Active,
                        isPrefabSession = s.IsPrefabSession
                    } );
                }
            }
        }
        catch { }

        return HandlerBase.Success( new
        {
            unsavedCount = results.Count,
            message = results.Count > 0 ? $"{results.Count} session(s) have unsaved changes." : "All sessions are saved.",
            unsaved = results
        } );
    }

    // ── get_status ────────────────────────────────────────────────────────
    // NEW action

    private static object GetStatus( JsonElement args )
    {
        var path = SceneHelpers.NormalizePath( HandlerBase.GetString( args, "path" ) );
        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "get_status" );

        var asset = AssetSystem.FindByPath( path );
        if ( asset == null )
            return HandlerBase.Error( $"Asset not found: '{path}'.", "get_status" );

        long fileSize = 0;
        try
        {
            if ( !string.IsNullOrEmpty( asset.AbsolutePath ) && System.IO.File.Exists( asset.AbsolutePath ) )
                fileSize = new System.IO.FileInfo( asset.AbsolutePath ).Length;
        }
        catch { }

        return HandlerBase.Success( new
        {
            path,
            name = asset.Name,
            type = asset.AssetType?.FriendlyName ?? "",
            extension = asset.AssetType?.FileExtension ?? "",
            absolutePath = asset.AbsolutePath ?? "",
            fileSize,
            fileSizeFormatted = fileSize > 1024 * 1024
                ? $"{fileSize / (1024.0 * 1024.0):F1} MB"
                : fileSize > 1024
                    ? $"{fileSize / 1024.0:F1} KB"
                    : $"{fileSize} bytes"
        } );
    }

    // ── get_json ──────────────────────────────────────────────────────────
    // NEW action

    private static object GetJson( JsonElement args )
    {
        var path = SceneHelpers.NormalizePath( HandlerBase.GetString( args, "path" ) );
        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "get_json" );

        var asset = AssetSystem.FindByPath( path );
        if ( asset == null )
            return HandlerBase.Error( $"Asset not found: '{path}'.", "get_json" );

        if ( string.IsNullOrEmpty( asset.AbsolutePath ) || !System.IO.File.Exists( asset.AbsolutePath ) )
            return HandlerBase.Error( $"Asset file not found on disk: '{path}'.", "get_json" );

        var raw = System.IO.File.ReadAllText( asset.AbsolutePath );

        if ( raw.Length > HandlerBase.MaxResponseChars - 200 )
            raw = raw[..(HandlerBase.MaxResponseChars - 200)] + "\n... [truncated]";

        return HandlerBase.Text( raw );
    }

    // ── get_references ────────────────────────────────────────────────────
    // NEW action — shared implementation also used by asset_manage.get_references

    internal static object GetReferences( JsonElement args )
    {
        var path = SceneHelpers.NormalizePath( HandlerBase.GetString( args, "path" ) );
        var deep = HandlerBase.GetBool( args, "deep", false );

        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "get_references" );

        var asset = AssetSystem.FindByPath( path );
        if ( asset == null )
            return HandlerBase.Error( $"Asset not found: '{path}'.", "get_references" );

        // Assets this path references (outgoing)
        var outgoing = new List<string>();
        try
        {
            var visited = new HashSet<string>();
            if ( deep )
                CollectDependencies( asset, outgoing, visited );
            else
                foreach ( var dep in asset.GetReferences( false ) )
                    outgoing.Add( dep?.Path ?? "?" );
        }
        catch { }

        // Assets that reference this path (incoming) — scan all assets
        var incoming = new List<string>();
        try
        {
            foreach ( var other in AssetSystem.All )
            {
                if ( other.Path == path ) continue;
                try
                {
                    foreach ( var dep in other.GetReferences( false ) )
                    {
                        if ( dep?.Path == path )
                        {
                            incoming.Add( other.Path );
                            break;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return HandlerBase.Success( new
        {
            asset = path,
            referencedBy = incoming,
            referencedByCount = incoming.Count,
            references = outgoing,
            referencesCount = outgoing.Count
        } );
    }
}
