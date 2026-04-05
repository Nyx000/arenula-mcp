using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// scene tool: summary, hierarchy, statistics, find, find_in_radius, get_details, prefab_instances.
/// All actions are read-only.
/// Ported from Ozmium SceneToolHandlers + OzmiumAssetHandlers.GetSceneStatistics.
/// </summary>
internal static class SceneHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "summary"          => Summary(),
                "hierarchy"        => Hierarchy( args ),
                "statistics"       => Statistics(),
                "find"             => Find( args ),
                "find_in_radius"   => FindInRadius( args ),
                "get_details"      => GetDetails( args ),
                "prefab_instances" => PrefabInstances( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}' for tool 'scene'.", action,
                    "Valid actions: summary, hierarchy, statistics, find, find_in_radius, get_details, prefab_instances" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── summary ───────────────────────────────────────────────────────

    private static object Summary()
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene. Open a scene or prefab in the editor.", "summary" );

        var allObjects = SceneHelpers.WalkAll( scene, includeDisabled: true ).ToList();
        var rootObjects = scene.Children.ToList();

        // Component type frequency
        var compCounts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
        foreach ( var go in allObjects )
            foreach ( var comp in go.Components.GetAll() )
            {
                var typeName = comp.GetType().Name;
                compCounts.TryGetValue( typeName, out var existing );
                compCounts[typeName] = existing + 1;
            }

        // Network mode distribution
        var netModeCounts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
        foreach ( var go in allObjects )
        {
            var mode = go.NetworkMode.ToString();
            netModeCounts.TryGetValue( mode, out var existing );
            netModeCounts[mode] = existing + 1;
        }

        // Root object quick list
        var rootList = rootObjects.Select( g => new
        {
            id = g.Id.ToString(),
            name = g.Name,
            enabled = g.Enabled,
            childCount = g.Children.Count,
            components = SceneHelpers.GetComponentNames( g )
        } ).ToList();

        return HandlerBase.Success( new
        {
            sceneName = scene.Name,
            totalObjects = allObjects.Count,
            rootObjects = rootObjects.Count,
            enabledObjects = allObjects.Count( g => g.Enabled ),
            disabledObjects = allObjects.Count( g => !g.Enabled ),
            componentBreakdown = compCounts
                .OrderByDescending( kv => kv.Value )
                .Select( kv => new { type = kv.Key, count = kv.Value } ),
            networkModeBreakdown = netModeCounts,
            rootObjectList = rootList
        } );
    }

    // ── hierarchy ─────────────────────────────────────────────────────

    private static object Hierarchy( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene. Open a scene or prefab in the editor.", "hierarchy" );

        int maxDepth = HandlerBase.GetInt( args, "max_depth", -1 );
        var sb = new StringBuilder();
        sb.AppendLine( $"Scene: {scene.Name}" );

        foreach ( var root in scene.Children )
            WalkHierarchy( sb, root, 0, maxDepth );

        return HandlerBase.Text( sb.ToString() );
    }

    private static void WalkHierarchy( StringBuilder sb, GameObject go, int depth, int maxDepth )
    {
        if ( go.Name != null && go.Name.IndexOf( SceneHelpers.IgnoreMarker, StringComparison.OrdinalIgnoreCase ) >= 0 ) return;
        if ( go.Tags.Has( SceneHelpers.IgnoreTag ) ) return;

        SceneHelpers.AppendHierarchyLine( sb, go, depth );

        if ( maxDepth >= 0 && depth >= maxDepth ) return;

        foreach ( var child in go.Children )
            WalkHierarchy( sb, child, depth + 1, maxDepth );
    }

    // ── statistics ────────────────────────────────────────────────────

    private static object Statistics()
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "statistics" );

        var allObjects = SceneHelpers.WalkAll( scene, includeDisabled: true ).ToList();

        // Component type frequency
        var compCounts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
        foreach ( var go in allObjects )
            foreach ( var comp in go.Components.GetAll() )
            {
                var typeName = comp.GetType().Name;
                compCounts.TryGetValue( typeName, out var existing );
                compCounts[typeName] = existing + 1;
            }

        // All unique tags
        var allTags = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        foreach ( var go in allObjects )
            foreach ( var tag in go.Tags.TryGetAll() )
                allTags.Add( tag );

        // Prefab source breakdown
        var prefabCounts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
        foreach ( var go in allObjects.Where( g => g.IsPrefabInstance && g.PrefabInstanceSource != null ) )
        {
            var src = go.PrefabInstanceSource;
            prefabCounts.TryGetValue( src, out var existing );
            prefabCounts[src] = existing + 1;
        }

        // Network mode distribution
        var netModeCounts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
        foreach ( var go in allObjects )
        {
            var mode = go.NetworkMode.ToString();
            netModeCounts.TryGetValue( mode, out var existing );
            netModeCounts[mode] = existing + 1;
        }

        return HandlerBase.Success( new
        {
            sceneName = scene.Name,
            totalObjects = allObjects.Count,
            rootObjects = scene.Children.Count,
            enabledObjects = allObjects.Count( g => g.Enabled ),
            disabledObjects = allObjects.Count( g => !g.Enabled ),
            uniqueTags = allTags.OrderBy( t => t ).ToList(),
            componentBreakdown = compCounts
                .OrderByDescending( kv => kv.Value )
                .Select( kv => new { type = kv.Key, count = kv.Value } )
                .ToList(),
            prefabBreakdown = prefabCounts
                .OrderByDescending( kv => kv.Value )
                .Select( kv => new { prefab = kv.Key, instances = kv.Value } )
                .ToList(),
            networkModeBreakdown = netModeCounts
        } );
    }

    // ── find ──────────────────────────────────────────────────────────

    private static object Find( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "find" );

        var query = HandlerBase.GetString( args, "query" );
        var tag = HandlerBase.GetString( args, "tag" );
        var componentType = HandlerBase.GetString( args, "component_type" );
        int offset = HandlerBase.GetInt( args, "offset", 0 );
        int limit = HandlerBase.GetInt( args, "limit", 50 );
        var format = HandlerBase.GetString( args, "format", "concise" );
        bool detailed = format == "detailed";

        var matches = new List<GameObject>();

        foreach ( var go in SceneHelpers.WalkAll( scene, includeDisabled: true ) )
        {
            if ( !string.IsNullOrEmpty( query ) )
            {
                bool nameMatch = go.Name != null && go.Name.IndexOf( query, StringComparison.OrdinalIgnoreCase ) >= 0;
                bool pathMatch = SceneHelpers.GetObjectPath( go ).IndexOf( query, StringComparison.OrdinalIgnoreCase ) >= 0;
                if ( !nameMatch && !pathMatch ) continue;
            }

            if ( !string.IsNullOrEmpty( tag ) && !go.Tags.Has( tag ) ) continue;

            if ( !string.IsNullOrEmpty( componentType ) )
            {
                bool found = go.Components.GetAll().Any( c =>
                    c.GetType().Name.IndexOf( componentType, StringComparison.OrdinalIgnoreCase ) >= 0 );
                if ( !found ) continue;
            }

            matches.Add( go );
        }

        return HandlerBase.Paginate( matches, offset, limit,
            go => detailed ? SceneHelpers.BuildDetail( go ) : SceneHelpers.BuildSummary( go ) );
    }

    // ── find_in_radius ────────────────────────────────────────────────

    private static object FindInRadius( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "find_in_radius" );

        var posStr = HandlerBase.GetString( args, "position" );
        if ( string.IsNullOrEmpty( posStr ) )
            return HandlerBase.Error( "Missing required 'position' parameter (format: 'x,y,z').", "find_in_radius" );

        var origin = HandlerBase.ParseVector3( posStr );
        float radius = HandlerBase.GetFloat( args, "radius", 1000f );
        int offset = HandlerBase.GetInt( args, "offset", 0 );
        int limit = HandlerBase.GetInt( args, "limit", 50 );
        var format = HandlerBase.GetString( args, "format", "concise" );
        bool detailed = format == "detailed";

        float radiusSq = radius * radius;
        var matches = new List<(float dist, GameObject go)>();

        foreach ( var go in SceneHelpers.WalkAll( scene, includeDisabled: true ) )
        {
            var diff = go.WorldPosition - origin;
            var distSq = diff.x * diff.x + diff.y * diff.y + diff.z * diff.z;
            if ( distSq > radiusSq ) continue;
            matches.Add( (MathF.Sqrt( distSq ), go) );
        }

        // Sort by distance (closest first)
        matches.Sort( ( a, b ) => a.dist.CompareTo( b.dist ) );
        var sorted = matches.Select( m => m.go ).ToList();

        return HandlerBase.Paginate( (IReadOnlyList<GameObject>)sorted, offset, limit, go =>
        {
            var dist = matches.First( m => m.go == go ).dist;
            if ( detailed )
            {
                var detail = SceneHelpers.BuildDetail( go );
                return new { detail, distanceFromOrigin = MathF.Round( dist, 2 ) };
            }
            else
            {
                var summary = SceneHelpers.BuildSummary( go );
                return new { summary, distanceFromOrigin = MathF.Round( dist, 2 ) };
            }
        } );
    }

    // ── get_details ───────────────────────────────────────────────────

    private static object GetDetails( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "get_details" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "get_details" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "get_details" );
        var format = HandlerBase.GetString( args, "format", "detailed" );

        if ( format == "concise" )
            return HandlerBase.Success( SceneHelpers.BuildSummary( go ) );

        return HandlerBase.Success( SceneHelpers.BuildDetail( go ) );
    }

    // ── prefab_instances ──────────────────────────────────────────────

    private static object PrefabInstances( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "prefab_instances" );

        var prefabPath = HandlerBase.GetString( args, "prefab_path" );
        int offset = HandlerBase.GetInt( args, "offset", 0 );
        int limit = HandlerBase.GetInt( args, "limit", 50 );

        // No prefabPath — return a breakdown of all prefab sources
        if ( string.IsNullOrEmpty( prefabPath ) )
        {
            var counts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
            foreach ( var go in SceneHelpers.WalkAll( scene, includeDisabled: true ) )
            {
                if ( !go.IsPrefabInstance || go.PrefabInstanceSource == null ) continue;
                counts.TryGetValue( go.PrefabInstanceSource, out var c );
                counts[go.PrefabInstanceSource] = c + 1;
            }

            var breakdown = counts
                .OrderByDescending( kv => kv.Value )
                .Select( kv => new { prefab = kv.Key, instances = kv.Value } )
                .ToList();

            return HandlerBase.Success( new
            {
                summary = $"{counts.Count} unique prefab(s) in scene.",
                breakdown
            } );
        }

        // Filter to specific prefab path
        var matches = new List<GameObject>();
        foreach ( var go in SceneHelpers.WalkAll( scene, includeDisabled: true ) )
        {
            if ( !go.IsPrefabInstance ) continue;
            if ( go.PrefabInstanceSource == null ) continue;
            if ( go.PrefabInstanceSource.IndexOf( prefabPath, StringComparison.OrdinalIgnoreCase ) < 0 ) continue;
            matches.Add( go );
        }

        return HandlerBase.Paginate( matches, offset, limit,
            go => SceneHelpers.BuildSummary( go ) );
    }
}
