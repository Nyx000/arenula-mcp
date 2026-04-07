using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// terrain tool: create, configure, get_info, paint_material, sync.
/// All actions are NEW (not ported from Ozmium).
/// Uses Terrain component + TerrainStorage + TerrainMaterial APIs.
/// </summary>
internal static class TerrainHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene found.", action, "Open a scene in the editor first." );

        return action switch
        {
            "create"         => Create( scene, args ),
            "configure"      => Configure( scene, args ),
            "get_info"       => GetInfo( scene, args ),
            "paint_material" => PaintMaterial( scene, args ),
            "sync"           => Sync( scene, args ),
            _                => HandlerBase.Error( $"Unknown action '{action}' for tool 'terrain'.", action,
                                    "Valid actions: create, configure, get_info, paint_material, sync" )
        };
    }

    // ── create ───────────────────────────────────────────────────────────

    private static object Create( Scene scene, JsonElement args )
    {
        var size = HandlerBase.GetFloat( args, "size", 1024f );
        var height = HandlerBase.GetFloat( args, "height", 512f );
        var resolution = HandlerBase.GetInt( args, "resolution", 512 );

        var go = scene.CreateObject();
        go.Name = "Terrain";

        var terrain = go.Components.Create<Terrain>();

        // Create storage with specified resolution
        var storage = new TerrainStorage();
        storage.SetResolution( resolution );
        storage.TerrainSize = size;
        storage.TerrainHeight = height;

        terrain.Storage = storage;
        terrain.TerrainSize = size;
        terrain.TerrainHeight = height;

        return HandlerBase.Success( new
        {
            id = go.Id.ToString(),
            name = go.Name,
            size,
            height,
            resolution,
            message = $"Created terrain ({size}x{size}, height {height}, resolution {resolution})."
        } );
    }

    // ── configure ────────────────────────────────────────────────────────

    private static object Configure( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "configure" );

        var lodLevels = HandlerBase.GetInt( args, "lod_levels", -1 );
        if ( lodLevels > 0 ) terrain.ClipMapLodLevels = lodLevels;

        var subdivision = HandlerBase.GetInt( args, "subdivision", -1 );
        if ( subdivision > 0 ) terrain.SubdivisionFactor = subdivision;

        var sizeVal = HandlerBase.GetFloat( args, "size", -1f );
        if ( sizeVal > 0 ) terrain.TerrainSize = sizeVal;

        var heightVal = HandlerBase.GetFloat( args, "height", -1f );
        if ( heightVal > 0 ) terrain.TerrainHeight = heightVal;

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            name = terrain.GameObject.Name,
            terrainSize = terrain.TerrainSize,
            terrainHeight = terrain.TerrainHeight,
            clipMapLodLevels = terrain.ClipMapLodLevels,
            subdivisionFactor = terrain.SubdivisionFactor,
            message = "Terrain configured."
        } );
    }

    // ── get_info ─────────────────────────────────────────────────────────

    private static object GetInfo( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "get_info" );

        var materials = new List<object>();
        if ( terrain.Storage?.Materials != null )
        {
            foreach ( var mat in terrain.Storage.Materials )
            {
                materials.Add( new
                {
                    name = mat?.ResourceName ?? "(unnamed)",
                    uvScale = mat?.UVScale ?? 1f,
                    metalness = mat?.Metalness ?? 0f,
                    normalStrength = mat?.NormalStrength ?? 1f,
                    hasHeight = mat?.HasHeightTexture ?? false
                } );
            }
        }

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            name = terrain.GameObject.Name,
            terrainSize = terrain.TerrainSize,
            terrainHeight = terrain.TerrainHeight,
            resolution = terrain.Storage?.Resolution ?? 0,
            clipMapLodLevels = terrain.ClipMapLodLevels,
            subdivisionFactor = terrain.SubdivisionFactor,
            materialCount = materials.Count,
            materials,
            hasHeightMap = terrain.HeightMap != null,
            hasControlMap = terrain.ControlMap != null,
            position = HandlerBase.V3( terrain.GameObject.WorldPosition )
        } );
    }

    // ── paint_material ───────────────────────────────────────────────────

    private static object PaintMaterial( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "paint_material" );

        var posStr = HandlerBase.GetString( args, "position" );
        if ( string.IsNullOrEmpty( posStr ) )
            return HandlerBase.Error( "Parameter 'position' is required for paint_material.", "paint_material",
                "Provide world position as 'x,y,z'." );

        var materialPath = HandlerBase.GetString( args, "material" );
        if ( string.IsNullOrEmpty( materialPath ) )
            return HandlerBase.Error( "Parameter 'material' is required for paint_material.", "paint_material",
                "Provide a terrain material asset path." );

        var worldPos = HandlerBase.ParseVector3( posStr );
        var radius = HandlerBase.GetFloat( args, "radius", 50f );
        var strength = HandlerBase.GetFloat( args, "strength", 1f );

        // Find the material index in the terrain's storage
        if ( terrain.Storage?.Materials == null || terrain.Storage.Materials.Count == 0 )
            return HandlerBase.Error( "Terrain has no materials configured.", "paint_material",
                "Add materials to the terrain's TerrainStorage first." );

        var materialIndex = -1;
        for ( int i = 0; i < terrain.Storage.Materials.Count; i++ )
        {
            var mat = terrain.Storage.Materials[i];
            if ( mat != null && mat.ResourceName != null &&
                 mat.ResourceName.IndexOf( materialPath, StringComparison.OrdinalIgnoreCase ) >= 0 )
            {
                materialIndex = i;
                break;
            }
        }

        if ( materialIndex < 0 )
        {
            var available = terrain.Storage.Materials
                .Where( m => m != null )
                .Select( m => m.ResourceName ?? "(unnamed)" )
                .ToList();
            return HandlerBase.Error( $"Material '{materialPath}' not found in terrain materials.",
                "paint_material",
                $"Available materials: {string.Join( ", ", available )}" );
        }

        // Paint by modifying the control map
        // The control map stores material indices per texel
        var storage = terrain.Storage;
        var res = storage.Resolution;
        if ( res <= 0 || storage.ControlMap == null )
            return HandlerBase.Error( "Terrain control map not initialized.", "paint_material" );

        // Convert world position to terrain-local UV coordinates
        var terrainPos = terrain.GameObject.WorldPosition;
        var terrainSize = terrain.TerrainSize;

        var localX = (worldPos.x - terrainPos.x) / terrainSize + 0.5f;
        var localY = (worldPos.y - terrainPos.y) / terrainSize + 0.5f;

        // Convert to texel coordinates
        var texelX = (int)(localX * res);
        var texelY = (int)(localY * res);
        var texelRadius = (int)(radius / terrainSize * res);

        var painted = 0;
        for ( int dy = -texelRadius; dy <= texelRadius; dy++ )
        {
            for ( int dx = -texelRadius; dx <= texelRadius; dx++ )
            {
                var px = texelX + dx;
                var py = texelY + dy;
                if ( px < 0 || px >= res || py < 0 || py >= res ) continue;

                var dist = MathF.Sqrt( dx * dx + dy * dy );
                if ( dist > texelRadius ) continue;

                var idx = py * res + px;
                if ( idx >= 0 && idx < storage.ControlMap.Length )
                {
                    // Set the material index in the control map
                    // Control map encoding: base material in lower bits
                    storage.ControlMap[idx] = (uint)materialIndex;
                    painted++;
                }
            }
        }

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            materialIndex,
            materialName = terrain.Storage.Materials[materialIndex]?.ResourceName,
            worldPosition = HandlerBase.V3( worldPos ),
            radius,
            strength,
            texelsPainted = painted,
            message = $"Painted {painted} texels with material index {materialIndex}. Call terrain.sync to apply changes."
        } );
    }

    // ── sync ─────────────────────────────────────────────────────────────

    private static object Sync( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "sync" );

        // Sync CPU textures to GPU (makes edits visible)
        terrain.SyncGPUTexture();

        // Also sync GPU back to CPU for saving — use full terrain resolution as region
        var res = terrain.Storage.Resolution;
        terrain.SyncCPUTexture(
            Terrain.SyncFlags.Height | Terrain.SyncFlags.Control,
            new RectInt( 0, 0, res, res ) );

        // Update materials buffer
        terrain.UpdateMaterialsBuffer();

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            name = terrain.GameObject.Name,
            message = "Terrain synced (CPU→GPU + GPU→CPU). Changes are now visible and saveable."
        } );
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Find the Terrain component by id param or by scanning the scene.</summary>
    private static Terrain FindTerrain( Scene scene, JsonElement args, string action )
    {
        var id = HandlerBase.GetString( args, "id" );

        if ( !string.IsNullOrEmpty( id ) )
        {
            var go = SceneHelpers.FindByIdOrThrow( scene, id, action );
            var terrain = go.Components.Get<Terrain>();
            if ( terrain == null )
                throw new ArgumentException( $"GameObject '{id}' does not have a Terrain component." );
            return terrain;
        }

        // No id provided — find the first Terrain in the scene
        var allTerrains = SceneHelpers.WalkAll( scene )
            .Select( go => go.Components.Get<Terrain>() )
            .Where( t => t != null )
            .ToList();

        if ( allTerrains.Count == 0 )
            throw new ArgumentException( "No Terrain found in scene. Use terrain.create first." );

        if ( allTerrains.Count > 1 )
            throw new ArgumentException( $"Multiple terrains found ({allTerrains.Count}). Provide 'id' to specify which one." );

        return allTerrains[0];
    }
}
