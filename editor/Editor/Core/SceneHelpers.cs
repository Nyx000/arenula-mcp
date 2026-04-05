using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// Scene resolution, tree walking, object lookup, and component helpers.
/// Ported from Ozmium OzmiumSceneHelpers + SceneQueryHelpers.
/// </summary>
internal static class SceneHelpers
{
    // ── Scene resolution ────────────────────────────────────────────────

    /// <summary>
    /// Returns the best available scene:
    /// 1. Active SceneEditorSession.Scene (editor)
    /// 2. First available editor session
    /// 3. Game.ActiveScene (play mode)
    /// 4. null
    /// </summary>
    internal static Scene ResolveScene()
    {
        try
        {
            var active = SceneEditorSession.Active;
            if ( active?.Scene != null ) return active.Scene;
            foreach ( var s in SceneEditorSession.All )
                if ( s?.Scene != null ) return s.Scene;
        }
        catch { }
        if ( Game.ActiveScene != null ) return Game.ActiveScene;
        return null;
    }

    // ── Tree walking ────────────────────────────────────────────────────

    internal const string IgnoreMarker = "(MCP IGNORE)";
    internal const string IgnoreTag = "mcp_ignore";
    internal const int MaxAutoWalkChildren = 25;

    internal static IEnumerable<GameObject> WalkAll( Scene scene, bool includeDisabled = true )
    {
        foreach ( var root in scene.Children )
            foreach ( var go in WalkSubtree( root, includeDisabled ) )
                yield return go;
    }

    internal static IEnumerable<GameObject> WalkSubtree( GameObject root, bool includeDisabled = true )
    {
        if ( !includeDisabled && !root.Enabled ) yield break;
        if ( root.Name != null && root.Name.IndexOf( IgnoreMarker, StringComparison.OrdinalIgnoreCase ) >= 0 ) yield break;
        if ( root.Tags.Has( IgnoreTag ) ) yield break;
        yield return root;
        if ( root.Children.Count > MaxAutoWalkChildren ) yield break;
        foreach ( var child in root.Children )
            foreach ( var go in WalkSubtree( child, includeDisabled ) )
                yield return go;
    }

    // ── Find by ID ──────────────────────────────────────────────────────

    internal static GameObject FindById( Scene scene, string id )
    {
        if ( !Guid.TryParse( id, out var guid ) ) return null;
        return WalkAll( scene ).FirstOrDefault( go => go.Id == guid );
    }

    internal static GameObject FindByIdOrThrow( Scene scene, string id, string action )
    {
        var go = FindById( scene, id );
        if ( go == null ) throw new ArgumentException( $"GameObject '{id}' not found in scene" );
        return go;
    }

    // ── Object path ─────────────────────────────────────────────────────

    internal static string GetObjectPath( GameObject go )
    {
        var parts = new List<string>();
        var cur = go;
        while ( cur != null ) { parts.Insert( 0, cur.Name ); cur = cur.Parent; }
        return string.Join( "/", parts );
    }

    // ── Component / tag helpers ─────────────────────────────────────────

    internal static List<string> GetComponentNames( GameObject go )
        => go.Components.GetAll().Select( c => c.GetType().Name ).ToList();

    internal static List<string> GetTags( GameObject go )
        => go.Tags.TryGetAll().ToList();

    // ── Object summary builder ──────────────────────────────────────────

    internal static object BuildSummary( GameObject go ) => new
    {
        id = go.Id.ToString(),
        name = go.Name,
        enabled = go.Enabled,
        tags = GetTags( go ),
        components = GetComponentNames( go ),
        position = HandlerBase.V3( go.WorldPosition ),
        childCount = go.Children.Count
    };

    internal static object BuildDetail( GameObject go ) => new
    {
        id = go.Id.ToString(),
        name = go.Name,
        path = GetObjectPath( go ),
        enabled = go.Enabled,
        tags = GetTags( go ),
        components = go.Components.GetAll().Select( c => new
        {
            id = c.Id.ToString(),
            type = c.GetType().Name,
            enabled = c.Enabled
        } ),
        worldTransform = new
        {
            position = HandlerBase.V3( go.WorldPosition ),
            rotation = HandlerBase.Rot( go.WorldRotation ),
            scale = HandlerBase.V3( go.WorldScale )
        },
        localTransform = new
        {
            position = HandlerBase.V3( go.LocalPosition ),
            rotation = HandlerBase.Rot( go.LocalRotation ),
            scale = HandlerBase.V3( go.LocalScale )
        },
        parent = go.Parent != null ? new { id = go.Parent.Id.ToString(), name = go.Parent.Name } : null,
        children = go.Children.Select( c => new { id = c.Id.ToString(), name = c.Name } ),
        isPrefabInstance = go.IsPrefabInstance
    };

    // ── Hierarchy builder ───────────────────────────────────────────────

    internal static void AppendHierarchyLine( StringBuilder sb, GameObject go, int depth )
    {
        var indent = new string( ' ', depth * 2 );
        var comps = GetComponentNames( go );
        var tags = GetTags( go );
        var compStr = comps.Count > 0 ? $" [{string.Join( ", ", comps )}]" : "";
        var tagStr = tags.Count > 0 ? $" #{string.Join( " #", tags )}" : "";
        var disStr = go.Enabled ? "" : " (disabled)";
        var childStr = go.Children.Count > MaxAutoWalkChildren ? $"  children:{go.Children.Count}" : "";
        sb.AppendLine( $"{indent}- {go.Name} (ID: {go.Id}){disStr}{tagStr}{compStr}{childStr}" );
    }

    // ── Asset path normalization ──────────────────────────────────────────

    /// <summary>
    /// Strips a leading "Assets/" or "assets/" prefix so AssetSystem.FindByPath works.
    /// </summary>
    internal static string NormalizePath( string path )
    {
        if ( path == null ) return null;
        if ( path.StartsWith( "Assets/", StringComparison.OrdinalIgnoreCase ) )
            path = path.Substring( "Assets/".Length );
        return path;
    }

    // ── Selection helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the currently selected GameObjects in the editor, using reflection
    /// to access the editor Selection API without hard dependencies.
    /// </summary>
    internal static List<GameObject> GetSelectedGameObjects()
    {
        var result = new List<GameObject>();
        try
        {
            var session = SceneEditorSession.Active;
            if ( session == null ) return result;
            var selProp = session.GetType().GetProperty( "Selection",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance );
            var selObj = selProp?.GetValue( session );
            if ( selObj == null ) return result;
            if ( selObj is IEnumerable<object> objs )
                foreach ( var o in objs )
                    if ( o is GameObject go ) result.Add( go );
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Sets the editor selection to a single GameObject using reflection.
    /// </summary>
    internal static bool SelectGameObject( GameObject go )
    {
        try
        {
            var session = SceneEditorSession.Active;
            if ( session == null ) return false;
            var selProp = session.GetType().GetProperty( "Selection",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance );
            var selObj = selProp?.GetValue( session );
            if ( selObj == null ) return false;
            var setMethod = selObj.GetType().GetMethod( "Set", new[] { typeof( GameObject ) } );
            setMethod?.Invoke( selObj, new object[] { go } );
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Clears the editor selection using reflection.
    /// </summary>
    internal static bool ClearSelection()
    {
        try
        {
            var session = SceneEditorSession.Active;
            if ( session == null ) return false;
            var selProp = session.GetType().GetProperty( "Selection",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance );
            var selObj = selProp?.GetValue( session );
            if ( selObj == null ) return false;
            var clearMethod = selObj.GetType().GetMethod( "Clear" );
            clearMethod?.Invoke( selObj, null );
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Adds a GameObject to the editor selection using reflection.
    /// </summary>
    internal static bool AddToSelection( GameObject go )
    {
        try
        {
            var session = SceneEditorSession.Active;
            if ( session == null ) return false;
            var selProp = session.GetType().GetProperty( "Selection",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance );
            var selObj = selProp?.GetValue( session );
            if ( selObj == null ) return false;
            var addMethod = selObj.GetType().GetMethod( "Add", new[] { typeof( object ) } );
            addMethod?.Invoke( selObj, new object[] { go } );
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Gets the world-space bounding box of a GameObject, trying collider bounds first,
    /// then ModelRenderer model bounds, falling back to a small box at the object's position.
    /// </summary>
    internal static BBox GetGameObjectBounds( GameObject go )
    {
        var collider = go.Components.GetAll().FirstOrDefault( c => c is Collider ) as Collider;
        if ( collider != null )
            return collider.GetWorldBounds();

        var modelRenderer = go.Components.GetAll()
            .FirstOrDefault( c => c.GetType().Name.Contains( "ModelRenderer" ) );
        if ( modelRenderer != null )
        {
            var prop = modelRenderer.GetType().GetProperty( "Model" );
            if ( prop != null )
            {
                var model = prop.GetValue( modelRenderer ) as Model;
                if ( model != null && !model.IsError && model.Bounds.Volume > 0 )
                    return model.Bounds;
            }
        }

        return BBox.FromPositionAndSize( go.WorldPosition, 1f );
    }
}
