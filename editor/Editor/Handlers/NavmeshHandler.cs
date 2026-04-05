// Editor/Handlers/NavmeshHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// navmesh tool: create_agent, create_area, create_link, generate, get_status, query_path.
/// Ported from NavigationToolHandlers.cs (200 lines).
/// NEW actions: generate, get_status, query_path — research required.
/// </summary>
internal static class NavmeshHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "create_agent" => CreateAgent( args ),
                "create_area"  => CreateArea( args ),
                "create_link"  => CreateLink( args ),
                "generate"     => Generate( args ),
                "get_status"   => GetStatus( args ),
                "query_path"   => QueryPath( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: create_agent, create_area, create_link, generate, get_status, query_path" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── create_agent ──────────────────────────────────────────────────
    // Ported from NavigationToolHandlers.CreateNavMeshAgent

    private static object CreateAgent( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_agent" );

        // Can add to existing GO (via id) or create a new one
        var id = HandlerBase.GetString( args, "id" );
        GameObject go;

        if ( !string.IsNullOrEmpty( id ) )
        {
            go = SceneHelpers.FindByIdOrThrow( scene, id, "create_agent" );
        }
        else
        {
            go = scene.CreateObject();
            go.Name = HandlerBase.GetString( args, "name" ) ?? "NavMesh Agent";
            var posStr = HandlerBase.GetString( args, "position" );
            if ( posStr != null ) go.WorldPosition = HandlerBase.ParseVector3( posStr );
        }

        var agent = go.Components.Create<NavMeshAgent>();

        if ( args.TryGetProperty( "height", out var hEl ) && hEl.ValueKind == JsonValueKind.Number )
            agent.Height = hEl.GetSingle();
        if ( args.TryGetProperty( "radius", out var rEl ) && rEl.ValueKind == JsonValueKind.Number )
            agent.Radius = rEl.GetSingle();
        if ( args.TryGetProperty( "speed", out var sEl ) && sEl.ValueKind == JsonValueKind.Number )
            agent.MaxSpeed = sEl.GetSingle();
        if ( args.TryGetProperty( "acceleration", out var aEl ) && aEl.ValueKind == JsonValueKind.Number )
            agent.Acceleration = aEl.GetSingle();
        if ( args.TryGetProperty( "update_position", out var upEl ) &&
             ( upEl.ValueKind == JsonValueKind.True || upEl.ValueKind == JsonValueKind.False ) )
            agent.UpdatePosition = upEl.GetBoolean();
        if ( args.TryGetProperty( "update_rotation", out var urEl ) &&
             ( urEl.ValueKind == JsonValueKind.True || urEl.ValueKind == JsonValueKind.False ) )
            agent.UpdateRotation = urEl.GetBoolean();

        return HandlerBase.Success( new
        {
            message = $"Created NavMeshAgent on '{go.Name}'.",
            id = go.Id.ToString(),
            component_id = agent.Id.ToString(),
            speed = agent.MaxSpeed,
            radius = agent.Radius
        } );
    }

    // ── create_area ───────────────────────────────────────────────────
    // Ported from NavigationToolHandlers.CreateNavMeshArea

    private static object CreateArea( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_area" );

        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;

        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "NavMesh Area";
        go.WorldPosition = position;

        var area = go.Components.Create<NavMeshArea>();
        area.IsBlocker = HandlerBase.GetBool( args, "is_blocker", true );

        // Size via "size" param
        var sizeStr = HandlerBase.GetString( args, "size" );
        if ( sizeStr != null )
        {
            go.WorldScale = HandlerBase.ParseVector3( sizeStr );
        }

        return HandlerBase.Success( new
        {
            message = $"Created NavMeshArea '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition ),
            is_blocker = area.IsBlocker
        } );
    }

    // ── create_link ───────────────────────────────────────────────────
    // Ported from NavigationToolHandlers.CreateNavMeshLink

    private static object CreateLink( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_link" );

        var startStr = HandlerBase.GetString( args, "start_position" );
        var endStr = HandlerBase.GetString( args, "end_position" );

        if ( string.IsNullOrEmpty( startStr ) || string.IsNullOrEmpty( endStr ) )
            return HandlerBase.Error( "Missing required 'start_position' and 'end_position' parameters (as 'x,y,z').", "create_link" );

        var startPos = HandlerBase.ParseVector3( startStr );
        var endPos = HandlerBase.ParseVector3( endStr );

        // Place the GO at the midpoint
        var midpoint = ( startPos + endPos ) / 2f;

        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "NavMesh Link";
        go.WorldPosition = midpoint;

        var link = go.Components.Create<NavMeshLink>();
        link.LocalStartPosition = startPos - midpoint;
        link.LocalEndPosition = endPos - midpoint;

        if ( args.TryGetProperty( "bidirectional", out var bdEl ) &&
             ( bdEl.ValueKind == JsonValueKind.True || bdEl.ValueKind == JsonValueKind.False ) )
            link.IsBiDirectional = bdEl.GetBoolean();

        return HandlerBase.Success( new
        {
            message = $"Created NavMeshLink '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition ),
            start = HandlerBase.V3( startPos ),
            end = HandlerBase.V3( endPos )
        } );
    }

    // ── generate ──────────────────────────────────────────────────────
    // NEW — trigger navmesh build.
    // Research: Use Scene.NavMesh.Generate() or NavArea static API.
    // If API not found, return informative error.

    private static object Generate( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "generate" );

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Attempt to trigger navmesh generation via scene's NavMesh property
            // The exact API depends on the sbox-api research step.
            // Try reflection first to find the method
            var navMeshProp = scene.GetType().GetProperty( "NavMesh" );
            if ( navMeshProp != null )
            {
                var navMesh = navMeshProp.GetValue( scene );
                if ( navMesh != null )
                {
                    var generateMethod = navMesh.GetType().GetMethod( "Generate" )
                        ?? navMesh.GetType().GetMethod( "Build" )
                        ?? navMesh.GetType().GetMethod( "Rebuild" );
                    if ( generateMethod != null )
                    {
                        generateMethod.Invoke( navMesh, null );
                        sw.Stop();
                        return HandlerBase.Success( new
                        {
                            message = "Navmesh generation triggered.",
                            generation_time_ms = sw.ElapsedMilliseconds
                        } );
                    }
                }
            }

            // Fallback: try static NavMesh class
            var navMeshType = TypeLibrary.GetType( "NavMesh" )?.TargetType
                ?? Type.GetType( "Sandbox.NavMesh, Sandbox.Game" );
            if ( navMeshType != null )
            {
                var buildMethod = navMeshType.GetMethod( "Generate",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public )
                    ?? navMeshType.GetMethod( "Build",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public );
                if ( buildMethod != null )
                {
                    buildMethod.Invoke( null, null );
                    sw.Stop();
                    return HandlerBase.Success( new
                    {
                        message = "Navmesh generation triggered via static API.",
                        generation_time_ms = sw.ElapsedMilliseconds
                    } );
                }
            }

            return HandlerBase.Error(
                "NavMesh generation API not found. This may require manual rebuild in the editor. " +
                "Check if NavMesh.Generate() or Scene.NavMesh.Build() exists in the current s&box version.",
                "generate",
                "Use the editor's navmesh tools to generate manually." );
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( $"NavMesh generation failed: {ex.Message}", "generate" );
        }
    }

    // ── get_status ────────────────────────────────────────────────────
    // NEW — query navmesh state and agent count.

    private static object GetStatus( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "get_status" );

        var agents = SceneHelpers.WalkAll( scene )
            .SelectMany( go => go.Components.GetAll().OfType<NavMeshAgent>() )
            .ToList();

        var areas = SceneHelpers.WalkAll( scene )
            .SelectMany( go => go.Components.GetAll().OfType<NavMeshArea>() )
            .ToList();

        var links = SceneHelpers.WalkAll( scene )
            .SelectMany( go => go.Components.GetAll().OfType<NavMeshLink>() )
            .ToList();

        return HandlerBase.Success( new
        {
            agent_count = agents.Count,
            area_count = areas.Count,
            link_count = links.Count,
            agents = agents.Select( a => new
            {
                id = a.GameObject.Id.ToString(),
                name = a.GameObject.Name,
                speed = a.MaxSpeed,
                position = HandlerBase.V3( a.GameObject.WorldPosition )
            } )
        } );
    }

    // ── query_path ────────────────────────────────────────────────────
    // NEW — pathfind between two points.
    // Research: Use NavMesh.GetPath() or NavMesh.PathBuilder.

    private static object QueryPath( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "query_path" );

        var fromStr = HandlerBase.GetString( args, "from" );
        var toStr = HandlerBase.GetString( args, "to" );

        if ( string.IsNullOrEmpty( fromStr ) || string.IsNullOrEmpty( toStr ) )
            return HandlerBase.Error( "Missing required 'from' and 'to' parameters (as 'x,y,z').", "query_path" );

        var from = HandlerBase.ParseVector3( fromStr );
        var to = HandlerBase.ParseVector3( toStr );

        try
        {
            // Try to use NavMesh.PathBuilder or NavMesh.GetSimplePath
            // s&box uses NavMesh.PathBuilder pattern:
            //   var path = NavMesh.PathBuilder( from ).Build( to );
            //   or NavMesh.GetSimplePath( from, to )
            var navMeshType = TypeLibrary.GetType( "NavMesh" )?.TargetType
                ?? Type.GetType( "Sandbox.NavMesh, Sandbox.Game" );

            if ( navMeshType != null )
            {
                // Try GetSimplePath first
                var getSimplePath = navMeshType.GetMethod( "GetSimplePath",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                    null, new[] { typeof( Vector3 ), typeof( Vector3 ) }, null );

                if ( getSimplePath != null )
                {
                    var result = getSimplePath.Invoke( null, new object[] { from, to } );
                    if ( result is Vector3[] points )
                    {
                        float totalDist = 0;
                        for ( int i = 1; i < points.Length; i++ )
                            totalDist += Vector3.DistanceBetween( points[i - 1], points[i] );

                        return HandlerBase.Success( new
                        {
                            message = $"Path found: {points.Length} waypoints, {totalDist:F1} units.",
                            waypoints = points.Select( p => HandlerBase.V3( p ) ),
                            distance = MathF.Round( totalDist, 1 ),
                            waypoint_count = points.Length
                        } );
                    }
                    return HandlerBase.Error( "NavMesh.GetSimplePath returned null — no path found.", "query_path" );
                }

                // Try GetPathPoints
                var getPathPoints = navMeshType.GetMethod( "GetPathPoints",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public );
                if ( getPathPoints != null )
                {
                    var result = getPathPoints.Invoke( null, new object[] { from, to } );
                    if ( result is IEnumerable<Vector3> pathPoints )
                    {
                        var pts = pathPoints.ToList();
                        float totalDist = 0;
                        for ( int i = 1; i < pts.Count; i++ )
                            totalDist += Vector3.DistanceBetween( pts[i - 1], pts[i] );

                        return HandlerBase.Success( new
                        {
                            message = $"Path found: {pts.Count} waypoints, {totalDist:F1} units.",
                            waypoints = pts.Select( p => HandlerBase.V3( p ) ),
                            distance = MathF.Round( totalDist, 1 ),
                            waypoint_count = pts.Count
                        } );
                    }
                }
            }

            return HandlerBase.Error(
                "NavMesh path query API not found. This may not be available in the current s&box version.",
                "query_path",
                "NavMesh.GetSimplePath(from, to) and NavMesh.GetPathPoints(from, to) were both tried via reflection." );
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( $"Path query failed: {ex.Message}", "query_path" );
        }
    }
}
