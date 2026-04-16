using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// terrain tool: create, configure, get_info, get_height, get_height_region, set_height,
/// noise, erode, stamp, add_material, remove_material, get_material_at, blend_materials,
/// set_hole, paint_material, import_heightmap, export_heightmap, sync.
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
            "create"            => Create( scene, args ),
            "configure"         => Configure( scene, args ),
            "get_info"          => GetInfo( scene, args ),
            "get_height"        => GetHeight( scene, args ),
            "get_height_region" => GetHeightRegion( scene, args ),
            "set_height"        => SetHeight( scene, args ),
            "noise"             => ApplyNoise( scene, args ),
            "erode"             => Erode( scene, args ),
            "stamp"             => Stamp( scene, args ),
            "add_material"      => AddMaterial( scene, args ),
            "remove_material"   => RemoveMaterial( scene, args ),
            "get_material_at"   => GetMaterialAt( scene, args ),
            "blend_materials"   => BlendMaterials( scene, args ),
            "set_hole"          => SetHole( scene, args ),
            "paint_material"    => PaintMaterial( scene, args ),
            "import_heightmap"  => ImportHeightmap( scene, args ),
            "export_heightmap"  => ExportHeightmap( scene, args ),
            "sync"              => Sync( scene, args ),
            "configure_clutter" => ConfigureClutter( scene, args ),
            "regenerate_clutter" => RegenerateClutter( scene, args ),
            "clear_clutter"     => ClearClutter( scene, args ),
            _                   => HandlerBase.Error( $"Unknown action '{action}' for tool 'terrain'.", action,
                "Valid actions: create, configure, get_info, get_height, get_height_region, set_height, noise, erode, stamp, add_material, remove_material, get_material_at, blend_materials, set_hole, paint_material, import_heightmap, export_heightmap, sync, configure_clutter, regenerate_clutter, clear_clutter" )
        };
    }

    // ── create ───────────────────────────────────────────────────────────

    private static object Create( Scene scene, JsonElement args )
    {
        var size = HandlerBase.GetFloat( args, "size", 1024f );
        var height = HandlerBase.GetFloat( args, "height", 512f );
        var resolution = HandlerBase.GetInt( args, "resolution", 512 );
        var sceneName = scene.Name ?? "Terrain";
        var name = HandlerBase.GetString( args, "name", $"{sceneName} Terrain" );

        var go = scene.CreateObject();
        go.Name = name;

        var terrain = go.Components.Create<Terrain>();

        // Reuse the component's default storage if available (it has proper
        // asset system initialization), otherwise create a new one.
        var storage = terrain.Storage ?? new TerrainStorage();
        storage.SetResolution( resolution );

        // SetResolution updates the property but doesn't reallocate the backing
        // arrays. Force-allocate HeightMap and ControlMap to the requested size.
        var texelCount = resolution * resolution;
        if ( storage.HeightMap == null || storage.HeightMap.Length != texelCount )
            storage.HeightMap = new ushort[texelCount];
        if ( storage.ControlMap == null || storage.ControlMap.Length != texelCount )
            storage.ControlMap = new uint[texelCount];

        storage.TerrainSize = size;
        storage.TerrainHeight = height;

        terrain.Storage = storage;
        terrain.TerrainSize = size;
        terrain.TerrainHeight = height;

        // Persist storage as a file-backed asset — uses go.Name for the file path
        // so each terrain gets a unique file, avoiding asset cache collisions.
        TryPersistStorageAsFile( terrain );

        // Force requested dimensions and resolution back onto storage.
        // TryPersistStorageAsFile may have loaded a cached resource that
        // clobbers our size, height, and array allocations.
        if ( terrain.Storage != null )
        {
            terrain.Storage.SetResolution( resolution );
            if ( terrain.Storage.HeightMap == null || terrain.Storage.HeightMap.Length != texelCount )
                terrain.Storage.HeightMap = new ushort[texelCount];
            if ( terrain.Storage.ControlMap == null || terrain.Storage.ControlMap.Length != texelCount )
                terrain.Storage.ControlMap = new uint[texelCount];
            terrain.Storage.TerrainSize = size;
            terrain.Storage.TerrainHeight = height;
        }
        terrain.TerrainSize = size;
        terrain.TerrainHeight = height;

        // Initialize terrain rendering from storage
        terrain.Create();

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

    // ── get_height ──────────────────────────────────────────────────

    private static object GetHeight( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "get_height" );

        var posStr = HandlerBase.GetString( args, "position" );
        if ( string.IsNullOrEmpty( posStr ) )
            return HandlerBase.Error( "Parameter 'position' is required.", "get_height" );

        var storage = terrain.Storage;
        if ( storage?.HeightMap == null )
            return HandlerBase.Error( "Terrain heightmap not initialized.", "get_height" );

        var worldPos = HandlerBase.ParseVector3( posStr );
        var res = storage.Resolution;
        var terrainPos = terrain.GameObject.WorldPosition;
        var terrainSize = terrain.TerrainSize;

        var localX = ( worldPos.x - terrainPos.x ) / terrainSize;
        var localY = ( worldPos.y - terrainPos.y ) / terrainSize;

        if ( localX < 0 || localX > 1 || localY < 0 || localY > 1 )
            return HandlerBase.Error( "Position is outside terrain bounds.", "get_height" );

        var texelX = Math.Clamp( (int)( localX * res ), 0, res - 1 );
        var texelY = Math.Clamp( (int)( localY * res ), 0, res - 1 );
        var idx = texelY * res + texelX;

        var worldHeight = storage.HeightMap[idx] / 65535.0f * terrain.TerrainHeight;

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            worldPosition = HandlerBase.V3( worldPos ),
            height = MathF.Round( worldHeight, 3 ),
            normalizedHeight = MathF.Round( storage.HeightMap[idx] / 65535.0f, 4 ),
            terrainHeight = terrain.TerrainHeight
        } );
    }

    // ── noise ────────────────────────────────────────────────────────

    private static object ApplyNoise( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "noise" );
        var storage = terrain.Storage;
        if ( storage?.HeightMap == null )
            return HandlerBase.Error( "Terrain heightmap not initialized.", "noise" );

        var noiseType = HandlerBase.GetString( args, "type", "perlin" );
        var scale = HandlerBase.GetFloat( args, "scale", 0.01f );
        var amplitude = HandlerBase.GetFloat( args, "amplitude", terrain.TerrainHeight * 0.3f );
        var octaves = HandlerBase.GetInt( args, "octaves", 4 );
        var persistence = HandlerBase.GetFloat( args, "persistence", 0.5f );
        var lacunarity = HandlerBase.GetFloat( args, "lacunarity", 2.0f );
        var seed = HandlerBase.GetInt( args, "seed", 42 );
        var mode = HandlerBase.GetString( args, "mode", "set" );
        var offsetX = HandlerBase.GetFloat( args, "offset_x", 0f );
        var offsetY = HandlerBase.GetFloat( args, "offset_y", 0f );

        var res = storage.Resolution;
        var terrainHeight = terrain.TerrainHeight;
        var perm = GeneratePermutation( seed );

        for ( int y = 0; y < res; y++ )
        {
            for ( int x = 0; x < res; x++ )
            {
                var nx = ( x + offsetX ) * scale;
                var ny = ( y + offsetY ) * scale;

                var noiseVal = FbmNoise( nx, ny, octaves, persistence, lacunarity, perm );

                if ( noiseType == "ridged" )
                    noiseVal = 1.0f - MathF.Abs( noiseVal * 2.0f - 1.0f );
                else if ( noiseType == "billow" )
                    noiseVal = MathF.Abs( noiseVal * 2.0f - 1.0f );

                var noiseHeight = noiseVal * amplitude;
                var idx = y * res + x;
                var current = storage.HeightMap[idx] / 65535.0f * terrainHeight;

                float newHeight;
                switch ( mode )
                {
                    case "add":
                        newHeight = current + noiseHeight;
                        break;
                    case "multiply":
                        newHeight = current * noiseVal;
                        break;
                    default: // "set"
                        newHeight = noiseHeight;
                        break;
                }

                newHeight = Math.Clamp( newHeight, 0f, terrainHeight );
                storage.HeightMap[idx] = (ushort)( newHeight / terrainHeight * 65535f );
            }
        }

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            noiseType,
            scale,
            amplitude,
            octaves,
            seed,
            mode,
            resolution = res,
            message = $"Applied {noiseType} noise ({res}x{res}, {octaves} octaves). Call terrain.sync to apply."
        } );
    }

    // ── erode ─────────────────────────────────────────────────────────
    // Particle-based hydraulic erosion (Hans Theobald Beyer algorithm)

    private static object Erode( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "erode" );
        var storage = terrain.Storage;
        if ( storage?.HeightMap == null )
            return HandlerBase.Error( "Terrain heightmap not initialized.", "erode" );

        var iterations = HandlerBase.GetInt( args, "iterations", 50000 );
        var erosionRate = HandlerBase.GetFloat( args, "erosion_rate", 0.3f );
        var depositionRate = HandlerBase.GetFloat( args, "deposition_rate", 0.3f );
        var evaporationRate = HandlerBase.GetFloat( args, "evaporation_rate", 0.01f );
        var gravity = HandlerBase.GetFloat( args, "gravity", 4f );
        var inertia = HandlerBase.GetFloat( args, "inertia", 0.05f );
        var capacityFactor = HandlerBase.GetFloat( args, "capacity", 4f );
        var minCapacity = HandlerBase.GetFloat( args, "min_capacity", 0.01f );
        var maxLifetime = HandlerBase.GetInt( args, "lifetime", 30 );
        var brushRadius = HandlerBase.GetInt( args, "erosion_radius", 3 );
        var seed = HandlerBase.GetInt( args, "seed", 42 );

        var res = storage.Resolution;
        var terrainHeight = terrain.TerrainHeight;

        // Work with normalized floats for precision
        var hMap = new float[res * res];
        for ( int i = 0; i < hMap.Length; i++ )
            hMap[i] = storage.HeightMap[i] / 65535.0f;

        // Pre-compute erosion brush weights
        var brush = new List<(int dx, int dy, float w)>();
        float wSum = 0;
        for ( int dy = -brushRadius; dy <= brushRadius; dy++ )
        {
            for ( int dx = -brushRadius; dx <= brushRadius; dx++ )
            {
                float d = MathF.Sqrt( dx * dx + dy * dy );
                if ( d > brushRadius ) continue;
                float w = 1.0f - d / brushRadius;
                brush.Add( ( dx, dy, w ) );
                wSum += w;
            }
        }
        for ( int i = 0; i < brush.Count; i++ )
        {
            var ( dx, dy, w ) = brush[i];
            brush[i] = ( dx, dy, w / wSum );
        }

        var rng = new Random( seed );

        for ( int iter = 0; iter < iterations; iter++ )
        {
            float px = rng.NextSingle() * ( res - 2 ) + 1;
            float py = rng.NextSingle() * ( res - 2 ) + 1;
            float dx = 0, dy = 0, speed = 1, water = 1, sediment = 0;

            for ( int life = 0; life < maxLifetime; life++ )
            {
                int nx = (int)px, ny = (int)py;
                if ( nx < 1 || nx >= res - 1 || ny < 1 || ny >= res - 1 ) break;

                float cx = px - nx, cy = py - ny;
                int idx = ny * res + nx;

                // Bilinear interpolation for height + gradient
                float hNW = hMap[idx], hNE = hMap[idx + 1];
                float hSW = hMap[idx + res], hSE = hMap[idx + res + 1];

                float gx = ( hNE - hNW ) * ( 1 - cy ) + ( hSE - hSW ) * cy;
                float gy = ( hSW - hNW ) * ( 1 - cx ) + ( hSE - hNE ) * cx;
                float h = hNW * ( 1 - cx ) * ( 1 - cy ) + hNE * cx * ( 1 - cy )
                        + hSW * ( 1 - cx ) * cy + hSE * cx * cy;

                // Update direction with inertia
                dx = dx * inertia - gx * ( 1 - inertia );
                dy = dy * inertia - gy * ( 1 - inertia );
                float len = MathF.Sqrt( dx * dx + dy * dy );
                if ( len > 0 ) { dx /= len; dy /= len; }
                else { dx = rng.NextSingle() * 2 - 1; dy = rng.NextSingle() * 2 - 1; }

                float npx = px + dx, npy = py + dy;
                int nnx = (int)npx, nny = (int)npy;
                if ( nnx < 1 || nnx >= res - 1 || nny < 1 || nny >= res - 1 ) break;

                float ncx = npx - nnx, ncy = npy - nny;
                int nIdx = nny * res + nnx;
                float nh = hMap[nIdx] * ( 1 - ncx ) * ( 1 - ncy ) + hMap[nIdx + 1] * ncx * ( 1 - ncy )
                         + hMap[nIdx + res] * ( 1 - ncx ) * ncy + hMap[nIdx + res + 1] * ncx * ncy;

                float dh = nh - h;
                float cap = Math.Max( -dh * speed * water * capacityFactor, minCapacity );

                if ( sediment > cap || dh > 0 )
                {
                    // Deposit
                    float deposit = dh > 0 ? Math.Min( dh, sediment ) : ( sediment - cap ) * depositionRate;
                    sediment -= deposit;
                    hMap[idx] += deposit * ( 1 - cx ) * ( 1 - cy );
                    hMap[idx + 1] += deposit * cx * ( 1 - cy );
                    hMap[idx + res] += deposit * ( 1 - cx ) * cy;
                    hMap[idx + res + 1] += deposit * cx * cy;
                }
                else
                {
                    // Erode using brush
                    float erode = Math.Min( ( cap - sediment ) * erosionRate, -dh );
                    foreach ( var ( bx, by, bw ) in brush )
                    {
                        int ex = nx + bx, ey = ny + by;
                        if ( ex < 0 || ex >= res || ey < 0 || ey >= res ) continue;
                        int eIdx = ey * res + ex;
                        hMap[eIdx] = Math.Max( 0, hMap[eIdx] - erode * bw );
                    }
                    sediment += erode;
                }

                speed = MathF.Sqrt( Math.Max( 0, speed * speed + dh * gravity ) );
                water *= ( 1 - evaporationRate );
                px = npx; py = npy;
                if ( water < 0.001f ) break;
            }
        }

        // Write back
        for ( int i = 0; i < Math.Min( hMap.Length, storage.HeightMap.Length ); i++ )
            storage.HeightMap[i] = (ushort)( Math.Clamp( hMap[i], 0f, 1f ) * 65535f );

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            iterations, erosionRate, depositionRate, seed,
            message = $"Applied hydraulic erosion ({iterations} droplets). Call terrain.sync to apply."
        } );
    }

    // ── stamp ────────────────────────────────────────────────────────

    private static object Stamp( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "stamp" );
        var storage = terrain.Storage;
        if ( storage?.HeightMap == null )
            return HandlerBase.Error( "Terrain heightmap not initialized.", "stamp" );

        var stampType = HandlerBase.GetString( args, "type", "hill" );
        var posStr = HandlerBase.GetString( args, "position" );
        if ( string.IsNullOrEmpty( posStr ) )
            return HandlerBase.Error( "Parameter 'position' is required.", "stamp" );

        var height = HandlerBase.GetFloat( args, "height", 20f );
        var radius = HandlerBase.GetFloat( args, "radius", 50f );
        var mode = HandlerBase.GetString( args, "mode", "add" );
        var angle = HandlerBase.GetFloat( args, "angle", 0f ) * MathF.PI / 180f;

        var worldPos = HandlerBase.ParseVector3( posStr );
        var res = storage.Resolution;
        var terrainHeight = terrain.TerrainHeight;
        var terrainSize = terrain.TerrainSize;
        var terrainPos = terrain.GameObject.WorldPosition;

        var localX = ( worldPos.x - terrainPos.x ) / terrainSize;
        var localY = ( worldPos.y - terrainPos.y ) / terrainSize;
        var texelCX = (int)( localX * res );
        var texelCY = (int)( localY * res );
        var texelRadius = Math.Max( 1, (int)( radius / terrainSize * res ) );
        var sigma = texelRadius / 3.0f;

        var modified = 0;
        for ( int dy = -texelRadius; dy <= texelRadius; dy++ )
        {
            for ( int dx = -texelRadius; dx <= texelRadius; dx++ )
            {
                var px = texelCX + dx;
                var py = texelCY + dy;
                if ( px < 0 || px >= res || py < 0 || py >= res ) continue;

                var dist = MathF.Sqrt( dx * dx + dy * dy );
                if ( dist > texelRadius ) continue;

                // For ridge/valley, rotate and use perpendicular distance
                float rdx = dx, rdy = dy;
                if ( angle != 0 )
                {
                    float cos = MathF.Cos( angle ), sin = MathF.Sin( angle );
                    rdx = dx * cos + dy * sin;
                    rdy = -dx * sin + dy * cos;
                }

                float profile;
                switch ( stampType )
                {
                    case "crater":
                        var rimDist = texelRadius * 0.8f;
                        var rimSigma = texelRadius * 0.15f;
                        profile = -MathF.Exp( -dist * dist / ( 2 * sigma * sigma ) )
                                 + 0.6f * MathF.Exp( -( dist - rimDist ) * ( dist - rimDist ) / ( 2 * rimSigma * rimSigma ) );
                        break;
                    case "mesa":
                        var t = dist / texelRadius;
                        var edge = 0.7f;
                        profile = t < edge ? 1.0f : 1.0f - ( t - edge ) / ( 1.0f - edge );
                        profile = profile * profile * ( 3 - 2 * profile ); // smoothstep
                        break;
                    case "ridge":
                        profile = MathF.Exp( -rdy * rdy / ( 2 * sigma * sigma * 0.3f ) );
                        break;
                    case "valley":
                        profile = -MathF.Exp( -rdy * rdy / ( 2 * sigma * sigma * 0.3f ) );
                        break;
                    case "plateau":
                        var pt = dist / texelRadius;
                        profile = pt < 0.6f ? 1.0f : Math.Max( 0, 1.0f - ( pt - 0.6f ) / 0.4f );
                        break;
                    default: // "hill"
                        profile = MathF.Exp( -dist * dist / ( 2 * sigma * sigma ) );
                        break;
                }

                var idx = py * res + px;
                var current = storage.HeightMap[idx] / 65535.0f * terrainHeight;
                float newHeight;

                switch ( mode )
                {
                    case "set":
                        newHeight = height * profile;
                        break;
                    case "subtract":
                        newHeight = current - height * MathF.Abs( profile );
                        break;
                    default: // "add"
                        newHeight = current + height * profile;
                        break;
                }

                newHeight = Math.Clamp( newHeight, 0f, terrainHeight );
                storage.HeightMap[idx] = (ushort)( newHeight / terrainHeight * 65535f );
                modified++;
            }
        }

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            stampType, height, radius, mode,
            texelsModified = modified,
            message = $"Applied '{stampType}' stamp ({modified} texels). Call terrain.sync to apply."
        } );
    }

    // ── get_height_region ────────────────────────────────────────────

    private static object GetHeightRegion( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "get_height_region" );
        var storage = terrain.Storage;
        if ( storage?.HeightMap == null )
            return HandlerBase.Error( "Terrain heightmap not initialized.", "get_height_region" );

        var posStr = HandlerBase.GetString( args, "position" );
        if ( string.IsNullOrEmpty( posStr ) )
            return HandlerBase.Error( "Parameter 'position' is required (center of region).", "get_height_region" );

        var worldPos = HandlerBase.ParseVector3( posStr );
        var regionSize = HandlerBase.GetFloat( args, "region_size", 100f );
        var samples = HandlerBase.GetInt( args, "samples", 10 );

        var res = storage.Resolution;
        var terrainHeight = terrain.TerrainHeight;
        var terrainSize = terrain.TerrainSize;
        var terrainPos = terrain.GameObject.WorldPosition;

        var heights = new List<object>();
        var step = regionSize / ( samples - 1 );
        var startX = worldPos.x - regionSize / 2;
        var startY = worldPos.y - regionSize / 2;

        float minH = float.MaxValue, maxH = float.MinValue, sumH = 0;
        int count = 0;

        for ( int sy = 0; sy < samples; sy++ )
        {
            var row = new List<float>();
            for ( int sx = 0; sx < samples; sx++ )
            {
                var wx = startX + sx * step;
                var wy = startY + sy * step;

                var lx = ( wx - terrainPos.x ) / terrainSize;
                var ly = ( wy - terrainPos.y ) / terrainSize;

                float h = 0;
                if ( lx >= 0 && lx <= 1 && ly >= 0 && ly <= 1 )
                {
                    var tx = Math.Clamp( (int)( lx * res ), 0, res - 1 );
                    var ty = Math.Clamp( (int)( ly * res ), 0, res - 1 );
                    h = storage.HeightMap[ty * res + tx] / 65535.0f * terrainHeight;
                }

                row.Add( MathF.Round( h, 2 ) );
                minH = Math.Min( minH, h );
                maxH = Math.Max( maxH, h );
                sumH += h;
                count++;
            }
            heights.Add( row );
        }

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            center = HandlerBase.V3( worldPos ),
            regionSize, samples,
            minHeight = MathF.Round( minH, 2 ),
            maxHeight = MathF.Round( maxH, 2 ),
            avgHeight = MathF.Round( sumH / count, 2 ),
            heights
        } );
    }

    // ── blend_materials ──────────────────────────────────────────────

    private static object BlendMaterials( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "blend_materials" );
        var storage = terrain.Storage;
        if ( storage?.ControlMap == null )
            return HandlerBase.Error( "Terrain control map not initialized.", "blend_materials" );

        var posStr = HandlerBase.GetString( args, "position" );
        if ( string.IsNullOrEmpty( posStr ) )
            return HandlerBase.Error( "Parameter 'position' is required.", "blend_materials" );

        var baseIndex = HandlerBase.GetInt( args, "base_index", 0 );
        var overlayIndex = HandlerBase.GetInt( args, "overlay_index", 1 );
        var blend = HandlerBase.GetFloat( args, "blend", 0.5f );
        var radius = HandlerBase.GetFloat( args, "radius", 50f );

        if ( baseIndex < 0 || baseIndex > 31 )
            return HandlerBase.Error( "base_index must be 0-31.", "blend_materials" );
        if ( overlayIndex < 0 || overlayIndex > 31 )
            return HandlerBase.Error( "overlay_index must be 0-31.", "blend_materials" );

        var blendByte = (byte)Math.Clamp( (int)( blend * 255f ), 0, 255 );
        var worldPos = HandlerBase.ParseVector3( posStr );
        var res = storage.Resolution;
        var terrainPos = terrain.GameObject.WorldPosition;
        var terrainSize = terrain.TerrainSize;

        var localX = ( worldPos.x - terrainPos.x ) / terrainSize;
        var localY = ( worldPos.y - terrainPos.y ) / terrainSize;
        var texelX = (int)( localX * res );
        var texelY = (int)( localY * res );
        var texelRadius = Math.Max( 1, (int)( radius / terrainSize * res ) );

        // Pack: bits 0-4 = base, bits 5-9 = overlay, bits 10-17 = blend
        uint packedBase = (uint)( baseIndex & 0x1F )
                        | ( (uint)( overlayIndex & 0x1F ) << 5 )
                        | ( (uint)blendByte << 10 );

        var modified = 0;
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
                if ( idx < 0 || idx >= storage.ControlMap.Length ) continue;

                // Preserve hole flag (bit 31)
                uint packed = packedBase | ( storage.ControlMap[idx] & 0x80000000u );
                storage.ControlMap[idx] = packed;
                modified++;
            }
        }

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            baseIndex, overlayIndex,
            blend = MathF.Round( blend, 2 ),
            texelsModified = modified,
            message = $"Blended materials ({modified} texels, base={baseIndex}, overlay={overlayIndex}, blend={blend:F2}). Call terrain.sync to apply."
        } );
    }

    // ── get_material_at ──────────────────────────────────────────────

    private static object GetMaterialAt( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "get_material_at" );

        var posStr = HandlerBase.GetString( args, "position" );
        if ( string.IsNullOrEmpty( posStr ) )
            return HandlerBase.Error( "Parameter 'position' is required.", "get_material_at" );

        var worldPos = HandlerBase.ParseVector3( posStr );
        var info = terrain.GetMaterialAtWorldPosition( worldPos );

        if ( info == null )
            return HandlerBase.Error( "Position is outside terrain bounds.", "get_material_at" );

        var val = info.Value;
        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            worldPosition = HandlerBase.V3( worldPos ),
            baseMaterialIndex = val.BaseTextureId,
            baseMaterialName = val.BaseMaterial?.ResourceName,
            overlayMaterialIndex = val.OverlayTextureId,
            overlayMaterialName = val.OverlayMaterial?.ResourceName,
            blendFactor = val.BlendFactor,
            dominantMaterial = val.GetDominantMaterial()?.ResourceName,
            isHole = val.IsHole
        } );
    }

    // ── set_hole ─────────────────────────────────────────────────────

    private static object SetHole( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "set_hole" );
        var storage = terrain.Storage;
        if ( storage?.ControlMap == null )
            return HandlerBase.Error( "Terrain control map not initialized.", "set_hole" );

        var posStr = HandlerBase.GetString( args, "position" );
        if ( string.IsNullOrEmpty( posStr ) )
            return HandlerBase.Error( "Parameter 'position' is required.", "set_hole" );

        var enabled = HandlerBase.GetBool( args, "enabled", true );
        var radius = HandlerBase.GetFloat( args, "radius", 10f );

        var worldPos = HandlerBase.ParseVector3( posStr );
        var res = storage.Resolution;
        var terrainPos = terrain.GameObject.WorldPosition;
        var terrainSize = terrain.TerrainSize;

        var localX = ( worldPos.x - terrainPos.x ) / terrainSize;
        var localY = ( worldPos.y - terrainPos.y ) / terrainSize;
        var texelX = (int)( localX * res );
        var texelY = (int)( localY * res );
        var texelRadius = Math.Max( 1, (int)( radius / terrainSize * res ) );

        var modified = 0;
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
                if ( idx < 0 || idx >= storage.ControlMap.Length ) continue;

                // Hole flag is bit 31 of the packed control map value
                if ( enabled )
                    storage.ControlMap[idx] |= 0x80000000u;
                else
                    storage.ControlMap[idx] &= ~0x80000000u;
                modified++;
            }
        }

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            enabled,
            worldPosition = HandlerBase.V3( worldPos ),
            radius,
            texelsModified = modified,
            message = $"{( enabled ? "Punched" : "Filled" )} hole ({modified} texels). Call terrain.sync to apply."
        } );
    }

    // ── import_heightmap ─────────────────────────────────────────────

    private static object ImportHeightmap( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "import_heightmap" );
        var storage = terrain.Storage;
        if ( storage?.HeightMap == null )
            return HandlerBase.Error( "Terrain heightmap not initialized.", "import_heightmap" );

        var path = HandlerBase.GetString( args, "path" );
        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Parameter 'path' is required.", "import_heightmap",
                "Provide a path to a grayscale image (PNG, TGA, etc.)." );

        var absPath = HandlerBase.ResolveProjectPath( path ) ?? path;
        if ( !System.IO.File.Exists( absPath ) )
            return HandlerBase.Error( $"File not found: '{path}'.", "import_heightmap" );

        try
        {
            var bytes = System.IO.File.ReadAllBytes( absPath );
            var bitmap = Bitmap.CreateFromBytes( bytes );
            if ( bitmap == null || !bitmap.IsValid )
                return HandlerBase.Error( "Failed to load image. Ensure it is a valid image format.", "import_heightmap" );

            var res = storage.Resolution;
            var terrainHeight = terrain.TerrainHeight;

            // Resize if image doesn't match terrain resolution
            if ( bitmap.Width != res || bitmap.Height != res )
                bitmap = bitmap.Resize( res, res );

            var pixels = bitmap.GetPixels();
            for ( int i = 0; i < Math.Min( pixels.Length, storage.HeightMap.Length ); i++ )
            {
                // Use luminance (red channel for grayscale images)
                var luminance = pixels[i].r * 0.299f + pixels[i].g * 0.587f + pixels[i].b * 0.114f;
                storage.HeightMap[i] = (ushort)( Math.Clamp( luminance, 0f, 1f ) * 65535f );
            }

            bitmap.Dispose();

            return HandlerBase.Success( new
            {
                id = terrain.GameObject.Id.ToString(),
                sourcePath = path,
                resolution = res,
                message = $"Imported heightmap from '{path}' ({res}x{res}). Call terrain.sync to apply."
            } );
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( $"Failed to import heightmap: {ex.Message}", "import_heightmap" );
        }
    }

    // ── export_heightmap ─────────────────────────────────────────────

    private static object ExportHeightmap( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "export_heightmap" );
        var storage = terrain.Storage;
        if ( storage?.HeightMap == null )
            return HandlerBase.Error( "Terrain heightmap not initialized.", "export_heightmap" );

        var path = HandlerBase.GetString( args, "path" );
        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Parameter 'path' is required.", "export_heightmap",
                "Provide an output file path (e.g. 'heightmaps/terrain_export.png')." );

        try
        {
            var res = storage.Resolution;
            var bitmap = new Bitmap( res, res );

            for ( int y = 0; y < res; y++ )
            {
                for ( int x = 0; x < res; x++ )
                {
                    var idx = y * res + x;
                    var normalized = storage.HeightMap[idx] / 65535.0f;
                    bitmap.SetPixel( x, y, new Color( normalized, normalized, normalized, 1f ) );
                }
            }

            var pngData = bitmap.ToPng();
            bitmap.Dispose();

            var absPath = HandlerBase.ResolveProjectPath( path ) ?? path;
            var dir = System.IO.Path.GetDirectoryName( absPath );
            if ( !string.IsNullOrEmpty( dir ) && !System.IO.Directory.Exists( dir ) )
                System.IO.Directory.CreateDirectory( dir );

            System.IO.File.WriteAllBytes( absPath, pngData );

            return HandlerBase.Success( new
            {
                id = terrain.GameObject.Id.ToString(),
                path,
                absolutePath = absPath,
                resolution = res,
                fileSize = pngData.Length,
                message = $"Exported heightmap to '{path}' ({res}x{res}, {pngData.Length} bytes)."
            } );
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( $"Failed to export heightmap: {ex.Message}", "export_heightmap" );
        }
    }

    // ── add_material ────────────────────────────────────────────────

    private static object AddMaterial( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "add_material" );

        var materialPath = HandlerBase.GetString( args, "material_path" );

        TerrainMaterial terrainMat = null;

        if ( !string.IsNullOrEmpty( materialPath ) )
        {
            terrainMat = ResourceLibrary.Get<TerrainMaterial>( materialPath );
            if ( terrainMat == null )
                return HandlerBase.Error( $"Terrain material not found: '{materialPath}'.", "add_material",
                    "Provide a valid .terrain_material asset path, or omit to use the first available." );
        }
        else
        {
            // No path given — find the first available terrain material
            terrainMat = ResourceLibrary.GetAll<TerrainMaterial>().FirstOrDefault();
            if ( terrainMat == null )
                return HandlerBase.Error( "No terrain materials available in the project.", "add_material",
                    "Import or create a .terrain_material asset first." );
        }

        // Check if already present
        if ( terrain.Storage.Materials.Contains( terrainMat ) )
            return HandlerBase.Success( new
            {
                id = terrain.GameObject.Id.ToString(),
                materialIndex = terrain.Storage.Materials.IndexOf( terrainMat ),
                materialCount = terrain.Storage.Materials.Count,
                materialName = terrainMat.ResourceName ?? "(unnamed)",
                message = $"Material already present in terrain."
            } );

        terrain.Storage.Materials.Add( terrainMat );

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            materialIndex = terrain.Storage.Materials.Count - 1,
            materialCount = terrain.Storage.Materials.Count,
            materialName = terrainMat.ResourceName ?? "(unnamed)",
            message = $"Added terrain material '{terrainMat.ResourceName}'."
        } );
    }

    // ── remove_material ─────────────────────────────────────────────

    private static object RemoveMaterial( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "remove_material" );

        var materialPath = HandlerBase.GetString( args, "material_path" );
        var indexParam = HandlerBase.GetInt( args, "material_index", -1 );

        if ( terrain.Storage?.Materials == null || terrain.Storage.Materials.Count == 0 )
            return HandlerBase.Error( "Terrain has no materials to remove.", "remove_material" );

        int removeIndex = -1;

        if ( indexParam >= 0 )
        {
            if ( indexParam >= terrain.Storage.Materials.Count )
                return HandlerBase.Error(
                    $"Material index {indexParam} out of range (0-{terrain.Storage.Materials.Count - 1}).",
                    "remove_material" );
            removeIndex = indexParam;
        }
        else if ( !string.IsNullOrEmpty( materialPath ) )
        {
            for ( int i = 0; i < terrain.Storage.Materials.Count; i++ )
            {
                var mat = terrain.Storage.Materials[i];
                if ( mat != null && mat.ResourceName != null &&
                     mat.ResourceName.IndexOf( materialPath, StringComparison.OrdinalIgnoreCase ) >= 0 )
                {
                    removeIndex = i;
                    break;
                }
            }

            if ( removeIndex < 0 )
                return HandlerBase.Error( $"Material '{materialPath}' not found in terrain.", "remove_material" );
        }
        else
        {
            return HandlerBase.Error( "Provide 'material_path' or 'material_index' to identify the material.", "remove_material" );
        }

        var removedName = terrain.Storage.Materials[removeIndex]?.ResourceName ?? "(unnamed)";
        terrain.Storage.Materials.RemoveAt( removeIndex );

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            removedIndex = removeIndex,
            removedName,
            materialCount = terrain.Storage.Materials.Count,
            message = $"Removed material '{removedName}' (was index {removeIndex})."
        } );
    }

    // ── set_height ──────────────────────────────────────────────────

    private static object SetHeight( Scene scene, JsonElement args )
    {
        var terrain = FindTerrain( scene, args, "set_height" );

        var posStr = HandlerBase.GetString( args, "position" );
        if ( string.IsNullOrEmpty( posStr ) )
            return HandlerBase.Error( "Parameter 'position' is required.", "set_height",
                "Provide world position as 'x,y,z'." );

        var mode = HandlerBase.GetString( args, "mode", "set" );
        var height = HandlerBase.GetFloat( args, "height", 0f );
        var radius = HandlerBase.GetFloat( args, "radius", 50f );
        var strength = HandlerBase.GetFloat( args, "strength", 1f );
        var falloff = HandlerBase.GetString( args, "falloff", "linear" );

        var storage = terrain.Storage;
        if ( storage?.HeightMap == null )
            return HandlerBase.Error( "Terrain heightmap not initialized.", "set_height" );

        var res = storage.Resolution;
        if ( res <= 0 )
            return HandlerBase.Error( "Terrain resolution is invalid.", "set_height" );

        var terrainHeight = terrain.TerrainHeight;
        var terrainSize = terrain.TerrainSize;
        var terrainPos = terrain.GameObject.WorldPosition;
        var worldPos = HandlerBase.ParseVector3( posStr );

        // Convert world position to texel coordinates
        var localX = ( worldPos.x - terrainPos.x ) / terrainSize;
        var localY = ( worldPos.y - terrainPos.y ) / terrainSize;

        var texelX = (int)( localX * res );
        var texelY = (int)( localY * res );
        var texelRadius = Math.Max( 1, (int)( radius / terrainSize * res ) );

        // Read center height for flatten mode
        var centerIdx = Math.Clamp( texelY * res + texelX, 0, storage.HeightMap.Length - 1 );
        var centerWorldHeight = storage.HeightMap[centerIdx] / 65535.0f * terrainHeight;

        var modified = 0;
        for ( int dy = -texelRadius; dy <= texelRadius; dy++ )
        {
            for ( int dx = -texelRadius; dx <= texelRadius; dx++ )
            {
                var px = texelX + dx;
                var py = texelY + dy;
                if ( px < 0 || px >= res || py < 0 || py >= res ) continue;

                var dist = MathF.Sqrt( dx * dx + dy * dy );
                if ( dist > texelRadius ) continue;

                // Calculate brush falloff
                var t = dist / texelRadius;
                float brushStrength;
                if ( falloff == "smooth" )
                    brushStrength = strength * ( 1.0f - t * t * ( 3.0f - 2.0f * t ) ); // smoothstep
                else if ( falloff == "none" )
                    brushStrength = strength;
                else // "linear"
                    brushStrength = strength * ( 1.0f - t );

                var idx = py * res + px;
                if ( idx < 0 || idx >= storage.HeightMap.Length ) continue;

                var current = storage.HeightMap[idx] / 65535.0f * terrainHeight;
                float newHeight;

                switch ( mode )
                {
                    case "raise":
                        newHeight = current + height * brushStrength;
                        break;
                    case "lower":
                        newHeight = current - height * brushStrength;
                        break;
                    case "flatten":
                        newHeight = current + ( centerWorldHeight - current ) * brushStrength;
                        break;
                    case "smooth":
                        var avg = GetAverageHeight( storage, px, py, res, terrainHeight, 6 );
                        newHeight = current + ( avg - current ) * brushStrength;
                        break;
                    default: // "set"
                        newHeight = current + ( height - current ) * brushStrength;
                        break;
                }

                newHeight = Math.Clamp( newHeight, 0f, terrainHeight );
                storage.HeightMap[idx] = (ushort)( newHeight / terrainHeight * 65535f );
                modified++;
            }
        }

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            mode,
            worldPosition = HandlerBase.V3( worldPos ),
            height,
            radius,
            strength,
            texelsModified = modified,
            message = $"Modified {modified} heightmap texels ({mode}). Call terrain.sync to apply changes."
        } );
    }

    /// <summary>Average height of neighboring texels for smooth mode.</summary>
    private static float GetAverageHeight( TerrainStorage storage, int cx, int cy, int res, float terrainHeight, int kernelRadius )
    {
        float sum = 0;
        int count = 0;
        for ( int dy = -kernelRadius; dy <= kernelRadius; dy++ )
        {
            for ( int dx = -kernelRadius; dx <= kernelRadius; dx++ )
            {
                var px = cx + dx;
                var py = cy + dy;
                if ( px < 0 || px >= res || py < 0 || py >= res ) continue;
                var idx = py * res + px;
                if ( idx < 0 || idx >= storage.HeightMap.Length ) continue;
                sum += storage.HeightMap[idx] / 65535.0f * terrainHeight;
                count++;
            }
        }
        return count > 0 ? sum / count : 0f;
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
                 ( mat.ResourceName.Equals( materialPath, StringComparison.OrdinalIgnoreCase )
                   || ( mat.ResourcePath != null && (
                          mat.ResourcePath.Equals( materialPath, StringComparison.OrdinalIgnoreCase )
                          || System.IO.Path.GetFileNameWithoutExtension( mat.ResourcePath ).Equals( materialPath, StringComparison.OrdinalIgnoreCase )
                        ) ) ) )
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

        var localX = (worldPos.x - terrainPos.x) / terrainSize;
        var localY = (worldPos.y - terrainPos.y) / terrainSize;

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

        // Try to sync GPU back to CPU for saving — may fail if GPU texture
        // size doesn't match storage resolution (non-fatal, CPU data is
        // already correct when edits come from stamps/noise/erode).
        try
        {
            var res = terrain.Storage.Resolution;
            terrain.SyncCPUTexture(
                Terrain.SyncFlags.Height | Terrain.SyncFlags.Control,
                new RectInt( 0, 0, res, res ) );
        }
        catch ( Exception ex )
        {
            Log.Warning( $"[terrain] SyncCPUTexture skipped: {ex.Message}" );
        }

        // Update materials buffer
        terrain.UpdateMaterialsBuffer();

        // Persist storage as a file-backed .terrain asset in the project directory.
        var persisted = TryPersistStorageAsFile( terrain );

        return HandlerBase.Success( new
        {
            id = terrain.GameObject.Id.ToString(),
            name = terrain.GameObject.Name,
            storagePersisted = persisted,
            message = "Terrain synced (CPU→GPU + GPU→CPU). Changes are now visible and saveable."
                    + ( persisted ? " Storage saved to .terrain file." : "" )
        } );
    }

    // ── auto-persistence helpers ────────────────────────────────────

    /// <summary>
    /// Persist TerrainStorage as a file-backed .terrain asset so the scene serializes
    /// a real resource reference instead of null.
    ///
    /// The source .terrain JSON is written to the project directory (terrain_data/).
    /// The compiled .terrain_c is cached in sbox/core/terrain_data/ — this is a
    /// limitation of AssetSystem.CompileResource which always resolves relative paths
    /// against the engine's core mount. RegisterFile(absPath) returns null because the
    /// asset path collides with the existing core-mounted entry, so we fall back to
    /// FindByPath. A sync call regenerates the compiled cache if it's ever lost
    /// (e.g. after an engine update).
    /// </summary>
    private static bool TryPersistStorageAsFile( Terrain terrain )
    {
        try
        {
            var storage = terrain.Storage;
            if ( storage == null ) return false;

            var name = terrain.GameObject.Name?.ToLower().Replace( ' ', '_' ) ?? "terrain";
            var relativePath = $"terrain_data/{name}.terrain";
            var absPath = HandlerBase.ResolveProjectPath( relativePath );
            if ( absPath == null ) return false;

            var dir = System.IO.Path.GetDirectoryName( absPath );
            if ( !string.IsNullOrEmpty( dir ) && !System.IO.Directory.Exists( dir ) )
                System.IO.Directory.CreateDirectory( dir );

            // Always write current serialized data to the source file
            var json = storage.Serialize();
            var jsonText = json.ToJsonString();
            System.IO.File.WriteAllText( absPath, jsonText );

            // Compile the resource data so the engine has an up-to-date .terrain_c
            AssetSystem.CompileResource( relativePath, jsonText );

            // Find or register the asset so we can load the compiled resource
            var asset = AssetSystem.FindByPath( relativePath )
                     ?? AssetSystem.RegisterFile( absPath );

            if ( asset == null )
            {
                Log.Warning( $"[terrain] Asset not found after compile: {relativePath}" );
                return false;
            }

            // If storage already points to a valid file, the rewrite + recompile is enough
            var alreadyBacked = !string.IsNullOrEmpty( storage.ResourcePath )
                                && storage.ResourcePath != "sandbox.terrain";
            if ( alreadyBacked )
            {
                Log.Info( $"[terrain] Updated storage: {relativePath}" );
                return true;
            }

            // First time: load the compiled resource and assign to terrain.
            var loaded = asset.LoadResource<TerrainStorage>();
            if ( loaded != null )
            {
                terrain.Storage = loaded;
                Log.Info( $"[terrain] Storage persisted: {asset.Path}" );
                return true;
            }

            Log.Warning( $"[terrain] LoadResource<TerrainStorage> returned null (Path={asset.Path})" );
            return false;
        }
        catch ( Exception ex )
        {
            Log.Warning( $"[terrain] TryPersistStorageAsFile failed: {ex.Message}" );
            return false;
        }
    }

    // ── Perlin noise helpers ────────────────────────────────────────

    private static int[] GeneratePermutation( int seed )
    {
        var p = new int[512];
        var b = new int[256];
        for ( int i = 0; i < 256; i++ ) b[i] = i;
        var rng = new Random( seed );
        for ( int i = 255; i > 0; i-- )
        {
            int j = rng.Next( i + 1 );
            ( b[i], b[j] ) = ( b[j], b[i] );
        }
        for ( int i = 0; i < 512; i++ ) p[i] = b[i & 255];
        return p;
    }

    private static float PerlinNoise( float x, float y, int[] perm )
    {
        int xi = (int)MathF.Floor( x ) & 255;
        int yi = (int)MathF.Floor( y ) & 255;
        float xf = x - MathF.Floor( x );
        float yf = y - MathF.Floor( y );
        float u = xf * xf * xf * ( xf * ( xf * 6 - 15 ) + 10 );
        float v = yf * yf * yf * ( yf * ( yf * 6 - 15 ) + 10 );
        int aa = perm[perm[xi] + yi], ab = perm[perm[xi] + yi + 1];
        int ba = perm[perm[xi + 1] + yi], bb = perm[perm[xi + 1] + yi + 1];
        float g( int hash, float fx, float fy ) { int h = hash & 3; float a = h < 2 ? fx : fy, b = h < 2 ? fy : fx; return ( ( h & 1 ) == 0 ? a : -a ) + ( ( h & 2 ) == 0 ? b : -b ); }
        float x1 = g( aa, xf, yf ) + u * ( g( ba, xf - 1, yf ) - g( aa, xf, yf ) );
        float x2 = g( ab, xf, yf - 1 ) + u * ( g( bb, xf - 1, yf - 1 ) - g( ab, xf, yf - 1 ) );
        return ( x1 + v * ( x2 - x1 ) + 1 ) / 2;
    }

    private static float FbmNoise( float x, float y, int octaves, float persistence, float lacunarity, int[] perm )
    {
        float total = 0, amplitude = 1, frequency = 1, maxValue = 0;
        for ( int i = 0; i < octaves; i++ )
        {
            total += PerlinNoise( x * frequency, y * frequency, perm ) * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }
        return total / maxValue;
    }

    // ── configure_clutter ───────────────────────────────────────────

    private static object ConfigureClutter( Scene scene, JsonElement args )
    {
        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_clutter" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "configure_clutter" );
        var clutter = go.Components.Get<Sandbox.Clutter.ClutterComponent>();
        if ( clutter == null )
            return HandlerBase.Error( $"No ClutterComponent found on '{go.Name}'.", "configure_clutter" );

        var defPath = HandlerBase.GetString( args, "definition" );
        if ( !string.IsNullOrEmpty( defPath ) )
        {
            var def = ResourceLibrary.Get<Sandbox.Clutter.ClutterDefinition>( defPath );
            if ( def == null )
                return HandlerBase.Error(
                    $"ClutterDefinition not found at '{defPath}'.",
                    "configure_clutter",
                    "User-created .clutter files must live under 'Assets/'. Path is relative to Assets/ (e.g. 'volcano_ground.clutter' for Assets/volcano_ground.clutter, no 'Assets/' prefix)." );
            clutter.Clutter = def;
        }

        if ( args.TryGetProperty( "seed", out var sEl ) && sEl.ValueKind == JsonValueKind.Number )
            clutter.Seed = sEl.GetInt32();

        var modeStr = HandlerBase.GetString( args, "mode" );
        if ( !string.IsNullOrEmpty( modeStr ) )
        {
            if ( Enum.TryParse<Sandbox.Clutter.ClutterComponent.ClutterMode>( modeStr, true, out var cm ) )
                clutter.Mode = cm;
        }

        return HandlerBase.Confirm( $"Configured ClutterComponent on '{go.Name}': seed={clutter.Seed}, mode={clutter.Mode}." );
    }

    // ── regenerate_clutter ──────────────────────────────────────────

    private static object RegenerateClutter( Scene scene, JsonElement args )
    {
        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "regenerate_clutter" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "regenerate_clutter" );
        var clutter = go.Components.Get<Sandbox.Clutter.ClutterComponent>();
        if ( clutter == null )
            return HandlerBase.Error( $"No ClutterComponent found on '{go.Name}'.", "regenerate_clutter" );

        var minStr = HandlerBase.GetString( args, "bounds_min" );
        var maxStr = HandlerBase.GetString( args, "bounds_max" );

        if ( !string.IsNullOrEmpty( minStr ) && !string.IsNullOrEmpty( maxStr ) )
        {
            var min = HandlerBase.ParseVector3( minStr );
            var max = HandlerBase.ParseVector3( maxStr );
            clutter.InvalidateTilesInBounds( new BBox( min, max ) );
            return HandlerBase.Confirm( $"Regenerated clutter tiles in bounds on '{go.Name}'." );
        }

        clutter.Generate();
        return HandlerBase.Confirm( $"Regenerated all clutter on '{go.Name}'." );
    }

    // ── clear_clutter ───────────────────────────────────────────────

    private static object ClearClutter( Scene scene, JsonElement args )
    {
        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "clear_clutter" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "clear_clutter" );
        var clutter = go.Components.Get<Sandbox.Clutter.ClutterComponent>();
        if ( clutter == null )
            return HandlerBase.Error( $"No ClutterComponent found on '{go.Name}'.", "clear_clutter" );

        if ( clutter.Infinite )
            clutter.ClearInfinite();
        else
            clutter.Clear();

        return HandlerBase.Confirm( $"Cleared all clutter on '{go.Name}'." );
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>Find the Terrain component by id param or by scanning the scene.</summary>
    private static Terrain FindTerrain( Scene scene, JsonElement args, string action )
    {
        var id = HandlerBase.GetString( args, "id" );
        Terrain terrain = null;

        if ( !string.IsNullOrEmpty( id ) )
        {
            var go = SceneHelpers.FindByIdOrThrow( scene, id, action );
            terrain = go.Components.Get<Terrain>();
            if ( terrain == null )
                throw new ArgumentException( $"GameObject '{id}' does not have a Terrain component." );
        }
        else
        {
            // No id provided — find the first Terrain in the scene
            var allTerrains = SceneHelpers.WalkAll( scene )
                .Select( go => go.Components.Get<Terrain>() )
                .Where( t => t != null )
                .ToList();

            if ( allTerrains.Count == 0 )
                throw new ArgumentException( "No Terrain found in scene. Use terrain.create first." );

            if ( allTerrains.Count > 1 )
                throw new ArgumentException( $"Multiple terrains found ({allTerrains.Count}). Provide 'id' to specify which one." );

            terrain = allTerrains[0];
        }

        return terrain;
    }
}
