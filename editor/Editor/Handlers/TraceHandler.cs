// editor/Editor/Handlers/TraceHandler.cs
using System;
using System.Collections.Generic;
using System.Text.Json;
using Sandbox;

namespace Arenula;

/// <summary>
/// trace tool: ray, sphere_cast, box_cast, sample_grid, multi_ray.
/// Spatial intelligence via Scene.Trace fluent API.
/// </summary>
internal static class TraceHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "ray"          => Ray( args ),
                "sphere_cast"  => SphereCast( args ),
                "box_cast"     => BoxCast( args ),
                "sample_grid"  => SampleGrid( args ),
                "multi_ray"    => MultiRay( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: ray, sphere_cast, box_cast, sample_grid, multi_ray" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── Shared: apply tag/ignore filters to a trace ──────────────────

    private static SceneTrace ApplyFilters( SceneTrace trace, JsonElement args, Scene scene )
    {
        var tags = HandlerBase.GetString( args, "tags" );
        if ( !string.IsNullOrEmpty( tags ) )
        {
            var tagArr = tags.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
            trace = trace.WithAnyTags( tagArr );
        }

        var ignoreTags = HandlerBase.GetString( args, "ignore_tags" );
        if ( !string.IsNullOrEmpty( ignoreTags ) )
        {
            var ignoreArr = ignoreTags.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
            trace = trace.WithoutTags( ignoreArr );
        }

        var ignoreId = HandlerBase.GetString( args, "ignore_id" );
        if ( !string.IsNullOrEmpty( ignoreId ) && scene != null )
        {
            var ignoreGo = SceneHelpers.FindById( scene, ignoreId );
            if ( ignoreGo != null )
                trace = trace.IgnoreGameObjectHierarchy( ignoreGo );
        }

        if ( HandlerBase.GetBool( args, "hit_triggers", false ) )
            trace = trace.HitTriggers();

        return trace;
    }

    // ── Shared: terrain raycast fallback ───────────────────────────────
    // Terrain physics collider may not update after heightmap edits in the
    // same session. Use Terrain.RayIntersects (heightmap-based, no physics)
    // as a fallback when Scene.Trace misses.

    private static object TryTerrainFallback( Scene scene, Vector3 from, Vector3 to )
    {
        var direction = ( to - from ).Normal;
        var distance = Vector3.DistanceBetween( from, to );
        var ray = new Ray( from, direction );

        foreach ( var go in SceneHelpers.WalkAll( scene ) )
        {
            var terrain = go.Components.Get<Terrain>();
            if ( terrain == null ) continue;

            if ( terrain.RayIntersects( ray, distance, out var localPos ) )
            {
                // localPos is in terrain local space — convert to world
                var worldPos = go.WorldTransform.PointToWorld( localPos );
                return new
                {
                    hit = true,
                    position = HandlerBase.V3( worldPos ),
                    normal = new { x = 0, y = 0, z = 1 },
                    distance = MathF.Round( Vector3.DistanceBetween( from, worldPos ), 2 ),
                    fraction = MathF.Round( Vector3.DistanceBetween( from, worldPos ) / distance, 4 ),
                    start_position = HandlerBase.V3( from ),
                    end_position = HandlerBase.V3( worldPos ),
                    surface = "terrain",
                    tags = new[] { "solid" },
                    @object = new { id = go.Id.ToString(), name = go.Name },
                    component_type = "Terrain"
                };
            }
        }

        return null;
    }

    // ── Shared: run trace with terrain fallback ─────────────────────

    private static object RunWithFallback( Scene scene, SceneTrace trace, Vector3 from, Vector3 to )
    {
        var result = trace.Run();
        if ( result.Hit )
            return FormatResult( result );

        var fallback = TryTerrainFallback( scene, from, to );
        if ( fallback != null )
            return fallback;

        return FormatResult( result );
    }

    // ── Shared: format a SceneTraceResult into a response object ─────

    private static object FormatResult( SceneTraceResult r )
    {
        if ( !r.Hit )
        {
            return new
            {
                hit = false,
                start_position = HandlerBase.V3( r.StartPosition ),
                end_position = HandlerBase.V3( r.EndPosition ),
                distance = MathF.Round( r.Distance, 2 )
            };
        }

        return new
        {
            hit = true,
            position = HandlerBase.V3( r.EndPosition ),
            normal = HandlerBase.V3( r.Normal ),
            distance = MathF.Round( r.Distance, 2 ),
            fraction = MathF.Round( r.Fraction, 4 ),
            start_position = HandlerBase.V3( r.StartPosition ),
            end_position = HandlerBase.V3( r.EndPosition ),
            surface = r.Surface?.ResourceName,
            tags = r.Tags,
            @object = r.GameObject != null ? new { id = r.GameObject.Id.ToString(), name = r.GameObject.Name } : null,
            component_type = r.Component?.GetType().Name
        };
    }

    // ── ray ──────────────────────────────────────────────────────────

    private static object Ray( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "ray" );

        var fromStr = HandlerBase.GetString( args, "from" );
        var toStr = HandlerBase.GetString( args, "to" );
        if ( string.IsNullOrEmpty( fromStr ) || string.IsNullOrEmpty( toStr ) )
            return HandlerBase.Error( "Missing required 'from' and 'to' parameters (as 'x,y,z').", "ray" );

        var from = HandlerBase.ParseVector3( fromStr );
        var to = HandlerBase.ParseVector3( toStr );

        var trace = scene.Trace.Ray( from, to );
        trace = ApplyFilters( trace, args, scene );

        return HandlerBase.Success( RunWithFallback( scene, trace, from, to ) );
    }

    // ── sphere_cast ─────────────────────────────────────────────────

    private static object SphereCast( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "sphere_cast" );

        var fromStr = HandlerBase.GetString( args, "from" );
        var toStr = HandlerBase.GetString( args, "to" );
        if ( string.IsNullOrEmpty( fromStr ) || string.IsNullOrEmpty( toStr ) )
            return HandlerBase.Error( "Missing required 'from' and 'to' parameters (as 'x,y,z').", "sphere_cast" );

        var radius = HandlerBase.GetFloat( args, "radius" );
        if ( radius <= 0 )
            return HandlerBase.Error( "Missing or invalid 'radius' parameter (must be > 0).", "sphere_cast" );

        var from = HandlerBase.ParseVector3( fromStr );
        var to = HandlerBase.ParseVector3( toStr );

        var trace = scene.Trace.Sphere( radius, from, to );
        trace = ApplyFilters( trace, args, scene );
        var result = trace.Run();

        return HandlerBase.Success( FormatResult( result ) );
    }

    // ── box_cast ────────────────────────────────────────────────────

    private static object BoxCast( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "box_cast" );

        var fromStr = HandlerBase.GetString( args, "from" );
        var toStr = HandlerBase.GetString( args, "to" );
        var sizeStr = HandlerBase.GetString( args, "size" );
        if ( string.IsNullOrEmpty( fromStr ) || string.IsNullOrEmpty( toStr ) )
            return HandlerBase.Error( "Missing required 'from' and 'to' parameters (as 'x,y,z').", "box_cast" );
        if ( string.IsNullOrEmpty( sizeStr ) )
            return HandlerBase.Error( "Missing required 'size' parameter (box extents as 'x,y,z').", "box_cast" );

        var from = HandlerBase.ParseVector3( fromStr );
        var to = HandlerBase.ParseVector3( toStr );
        var extents = HandlerBase.ParseVector3( sizeStr );

        var trace = scene.Trace.Box( extents, from, to );
        trace = ApplyFilters( trace, args, scene );
        var result = trace.Run();

        return HandlerBase.Success( FormatResult( result ) );
    }

    // ── sample_grid ─────────────────────────────────────────────────

    // Returns a terrain-hit cell record for the given downward ray, or null
    // if no terrain is hit. Updates minZ/maxZ by ref when a hit is produced.
    // Extracted from SampleGrid to replace a `goto nextCell` across 3 nested
    // loops (refactor-cleanup.md Card 8).
    private static object TryTerrainGridCell(
        Scene scene, Vector3 from, Vector3 to,
        float roundedX, float roundedY, ref float minZ, ref float maxZ )
    {
        var fb = TryTerrainFallback( scene, from, to );
        if ( fb == null ) return null;

        foreach ( var tgo in SceneHelpers.WalkAll( scene ) )
        {
            var terr = tgo.Components.Get<Terrain>();
            if ( terr == null ) continue;
            var dir = ( to - from ).Normal;
            var dist = Vector3.DistanceBetween( from, to );
            if ( !terr.RayIntersects( new Ray( from, dir ), dist, out var lp ) ) continue;

            var wp = tgo.WorldTransform.PointToWorld( lp );
            var z = wp.z;
            if ( z < minZ ) minZ = z;
            if ( z > maxZ ) maxZ = z;
            return new
            {
                x = roundedX,
                y = roundedY,
                hit = true,
                z = MathF.Round( z, 2 ),
                surface = "terrain",
                object_name = tgo.Name
            };
        }
        return null;
    }

    private static object SampleGrid( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "sample_grid" );

        var centerStr = HandlerBase.GetString( args, "center" );
        var areaSizeStr = HandlerBase.GetString( args, "area_size" );
        if ( string.IsNullOrEmpty( centerStr ) || string.IsNullOrEmpty( areaSizeStr ) )
            return HandlerBase.Error( "Missing required 'center' (x,y,z) and 'area_size' (x,y) parameters.", "sample_grid" );

        var center = HandlerBase.ParseVector3( centerStr );
        var areaSize = HandlerBase.ParseVector2( areaSizeStr );
        var samplesX = Math.Clamp( HandlerBase.GetInt( args, "samples_x", 10 ), 1, 32 );
        var samplesY = Math.Clamp( HandlerBase.GetInt( args, "samples_y", 10 ), 1, 32 );
        var maxDepth = HandlerBase.GetFloat( args, "max_depth", 5000f );

        var grid = new List<object>();
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        int hits = 0;

        float startX = center.x - areaSize.x / 2f;
        float startY = center.y - areaSize.y / 2f;
        float stepX = samplesX > 1 ? areaSize.x / ( samplesX - 1 ) : 0;
        float stepY = samplesY > 1 ? areaSize.y / ( samplesY - 1 ) : 0;

        for ( int iy = 0; iy < samplesY; iy++ )
        {
            for ( int ix = 0; ix < samplesX; ix++ )
            {
                float x = startX + ix * stepX;
                float y = startY + iy * stepY;
                var from = new Vector3( x, y, center.z );
                var to = new Vector3( x, y, center.z - maxDepth );

                var trace = scene.Trace.Ray( from, to );
                trace = ApplyFilters( trace, args, scene );
                var result = trace.Run();

                // Terrain fallback if physics missed
                if ( !result.Hit )
                {
                    var terrainCell = TryTerrainGridCell( scene, from, to,
                        MathF.Round( x, 1 ), MathF.Round( y, 1 ), ref minZ, ref maxZ );
                    if ( terrainCell != null )
                    {
                        hits++;
                        grid.Add( terrainCell );
                    }
                    else
                    {
                        grid.Add( new
                        {
                            x = MathF.Round( x, 1 ),
                            y = MathF.Round( y, 1 ),
                            hit = false
                        } );
                    }
                }
                else
                {
                    hits++;
                    float z = result.EndPosition.z;
                    if ( z < minZ ) minZ = z;
                    if ( z > maxZ ) maxZ = z;
                    grid.Add( new
                    {
                        x = MathF.Round( x, 1 ),
                        y = MathF.Round( y, 1 ),
                        hit = true,
                        z = MathF.Round( z, 2 ),
                        surface = result.Surface?.ResourceName,
                        object_name = result.GameObject?.Name
                    } );
                }
            }
        }

        return HandlerBase.Success( new
        {
            grid,
            samples_x = samplesX,
            samples_y = samplesY,
            total_samples = samplesX * samplesY,
            hits,
            min_z = hits > 0 ? MathF.Round( minZ, 2 ) : (float?)null,
            max_z = hits > 0 ? MathF.Round( maxZ, 2 ) : (float?)null
        } );
    }

    // ── multi_ray ───────────────────────────────────────────────────

    private static object MultiRay( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "multi_ray" );

        if ( !args.TryGetProperty( "rays", out var raysEl ) || raysEl.ValueKind != JsonValueKind.Array )
            return HandlerBase.Error( "Missing required 'rays' parameter (JSON array of {from, to} objects).", "multi_ray" );

        var rayCount = raysEl.GetArrayLength();
        if ( rayCount > 256 )
            return HandlerBase.Error( $"Too many rays ({rayCount}). Maximum is 256 per call.", "multi_ray" );
        if ( rayCount == 0 )
            return HandlerBase.Error( "Empty 'rays' array.", "multi_ray" );

        var results = new List<object>();

        foreach ( var rayEl in raysEl.EnumerateArray() )
        {
            var fromStr = rayEl.TryGetProperty( "from", out var fEl ) && fEl.ValueKind == JsonValueKind.String
                ? fEl.GetString() : null;
            var toStr = rayEl.TryGetProperty( "to", out var tEl ) && tEl.ValueKind == JsonValueKind.String
                ? tEl.GetString() : null;

            if ( string.IsNullOrEmpty( fromStr ) || string.IsNullOrEmpty( toStr ) )
            {
                results.Add( new { hit = false, error = "Missing 'from' or 'to' in ray entry." } );
                continue;
            }

            var from = HandlerBase.ParseVector3( fromStr );
            var to = HandlerBase.ParseVector3( toStr );

            var trace = scene.Trace.Ray( from, to );
            trace = ApplyFilters( trace, args, scene );
            results.Add( RunWithFallback( scene, trace, from, to ) );
        }

        return HandlerBase.Success( new { results, count = results.Count } );
    }
}
