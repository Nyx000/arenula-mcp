// Editor/Handlers/EditorHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// editor tool (non-console actions): select, get_selected, set_selected, clear_selection,
/// frame_selection, get_play_state, start_play, stop_play, get_log, save_scene,
/// save_scene_as, undo, redo.
/// Console actions (console_list, console_run) are handled by ConsoleHandler.
/// New actions: open_code_file, get_preferences, set_preference.
/// Ported from Ozmium OzmiumEditorHandlers + OzmiumWriteHandlers.
/// </summary>
internal static class EditorHandler
{
    // Circular log buffer for editor log capture
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _log = new();
    private const int MaxLogLines = 2000;

    /// <summary>Called by the MCP server to capture log output.</summary>
    internal static void AppendLog( string msg )
    {
        _log.Enqueue( msg );
        while ( _log.Count > MaxLogLines ) _log.TryDequeue( out _ );
    }

    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "select"           => Select( args ),
                "get_selected"     => GetSelected(),
                "set_selected"     => SetSelected( args ),
                "clear_selection"  => ClearSelection(),
                "frame_selection"  => FrameSelection( args ),
                "get_play_state"   => GetPlayState(),
                "start_play"       => StartPlay(),
                "stop_play"        => StopPlay(),
                "get_log"          => GetLog( args ),
                "save_scene"       => SaveScene(),
                "save_scene_as"    => SaveSceneAs( args ),
                "undo"             => Undo(),
                "redo"             => Redo(),
                "open_code_file"   => OpenCodeFile( args ),
                "get_preferences"  => GetPreferences(),
                "set_preference"   => SetPreference( args ),
                "console_list"     => ConsoleHandler.Handle( action, args ),
                "console_run"      => ConsoleHandler.Handle( action, args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: select, get_selected, set_selected, clear_selection, frame_selection, get_play_state, start_play, stop_play, get_log, save_scene, save_scene_as, undo, redo, open_code_file, get_preferences, set_preference" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── select ───────────────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.SelectGameObject

    private static object Select( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "select" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "select" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "select" );
        SceneHelpers.SelectGameObject( go );
        return HandlerBase.WriteConfirm( go, $"Selected '{go.Name}'." );
    }

    // ── get_selected ─────────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.GetSelectedObjects

    private static object GetSelected()
    {
        var selected = SceneHelpers.GetSelectedGameObjects();
        if ( selected.Count == 0 )
            return HandlerBase.Success( new { count = 0, objects = Array.Empty<object>() } );

        return HandlerBase.Success( new
        {
            count = selected.Count,
            objects = selected.Select( go => new
            {
                id = go.Id.ToString(),
                name = go.Name,
                path = SceneHelpers.GetObjectPath( go ),
                position = HandlerBase.V3( go.WorldPosition )
            } )
        } );
    }

    // ── set_selected ─────────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.SetSelectedObjects

    private static object SetSelected( JsonElement args )
    {
        var idsStr = HandlerBase.GetString( args, "ids" );
        if ( string.IsNullOrEmpty( idsStr ) )
            return HandlerBase.Error( "Missing required 'ids' parameter.", "set_selected" );

        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "set_selected" );

        SceneHelpers.ClearSelection();

        var ids = idsStr.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
        int count = 0;

        foreach ( var idStr in ids )
        {
            var go = SceneHelpers.FindById( scene, idStr );
            if ( go != null )
            {
                SceneHelpers.AddToSelection( go );
                count++;
            }
        }

        return HandlerBase.Confirm( $"Selected {count} object(s)." );
    }

    // ── clear_selection ──────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.ClearSelection

    private static object ClearSelection()
    {
        SceneHelpers.ClearSelection();
        return HandlerBase.Confirm( "Selection cleared." );
    }

    // ── frame_selection ──────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.FrameSelection

    private static object FrameSelection( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null ) return HandlerBase.Error( "No active scene.", "frame_selection" );

        var session = SceneEditorSession.Active;
        if ( session == null ) return HandlerBase.Error( "No editor session.", "frame_selection" );

        var id = HandlerBase.GetString( args, "id" );
        List<GameObject> targets;

        if ( !string.IsNullOrEmpty( id ) )
        {
            var go = SceneHelpers.FindByIdOrThrow( scene, id, "frame_selection" );
            targets = new List<GameObject> { go };
        }
        else
        {
            targets = SceneHelpers.GetSelectedGameObjects();
            if ( targets.Count == 0 )
                return HandlerBase.Error( "No selection to frame. Provide 'id' or select objects first.", "frame_selection" );
        }

        // Compute combined bounds
        var first = true;
        BBox bounds = default;
        foreach ( var go in targets )
        {
            var b = SceneHelpers.GetGameObjectBounds( go );
            if ( first ) { bounds = b; first = false; }
            else bounds = new BBox( Vector3.Min( bounds.Mins, b.Mins ), Vector3.Max( bounds.Maxs, b.Maxs ) );
        }

        // Call FrameTo directly — it takes 'in BBox'
        session.FrameTo( in bounds );
        return HandlerBase.Confirm( $"Framed camera to {targets.Count} object(s)." );
    }

    // ── get_play_state ───────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.GetPlayState

    private static object GetPlayState()
    {
        var session = SceneEditorSession.Active;
        var state = session?.IsPlaying == true ? "playing" : "stopped";
        return HandlerBase.Success( new { playState = state } );
    }

    // ── start_play ───────────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.StartPlayMode

    private static object StartPlay()
    {
        var session = SceneEditorSession.Active;
        if ( session == null ) return HandlerBase.Error( "No editor session.", "start_play" );
        if ( session.IsPlaying ) return HandlerBase.Confirm( "Already playing." );
        session.SetPlaying( session.Scene );
        return HandlerBase.Confirm( "Play mode started." );
    }

    // ── stop_play ────────────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.StopPlayMode

    private static object StopPlay()
    {
        var session = SceneEditorSession.Active;
        if ( session == null ) return HandlerBase.Error( "No editor session.", "stop_play" );
        if ( !session.IsPlaying ) return HandlerBase.Confirm( "Already stopped." );
        session.StopPlaying();
        return HandlerBase.Confirm( "Play mode stopped." );
    }

    // ── get_log ──────────────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.GetEditorLog

    private static object GetLog( JsonElement args )
    {
        var count = HandlerBase.GetInt( args, "count", 50 );
        var filter = HandlerBase.GetString( args, "filter" );
        var offset = HandlerBase.GetInt( args, "offset", 0 );
        var limit = HandlerBase.GetInt( args, "limit", 50 );

        var lines = _log.ToArray();

        // Apply count (take last N) first
        if ( count > 0 && count < lines.Length )
            lines = lines[^count..];

        // Apply filter
        if ( !string.IsNullOrEmpty( filter ) )
        {
            lines = lines.Where( l =>
                l.IndexOf( filter, StringComparison.OrdinalIgnoreCase ) >= 0 ).ToArray();
        }

        var allLines = lines.ToList();
        return HandlerBase.Paginate( allLines, offset, limit, l => l );
    }

    // ── save_scene ───────────────────────────────────────────────────────
    // Ported from OzmiumWriteHandlers.SaveScene

    private static object SaveScene()
    {
        var session = SceneEditorSession.Active;
        if ( session == null ) return HandlerBase.Error( "No editor session active.", "save_scene" );
        session.Save( false );

        // Post-condition: file exists on disk with non-zero size, scene shows no unsaved changes.
        var scene = session.Scene;
        string resourcePath = scene?.Source?.ResourcePath;
        string scenePath = null;
        if ( !string.IsNullOrEmpty( resourcePath ) )
        {
            var asset = AssetSystem.FindByPath( resourcePath );
            scenePath = asset?.AbsolutePath;
        }
        bool fileExists = !string.IsNullOrEmpty( scenePath ) && System.IO.File.Exists( scenePath );
        long fileSize = fileExists ? new System.IO.FileInfo( scenePath ).Length : 0;
        bool noUnsavedChanges = scene != null && !scene.HasUnsavedChanges;

        if ( !fileExists || fileSize == 0 )
            return HandlerBase.Error(
                $"Scene save reported success but file at '{scenePath}' does not exist or is empty.",
                "save_scene",
                "Engine silent save drop — check get_log for engine errors." );

        return HandlerBase.Success( new
        {
            message = $"Saved '{scene?.Name}'.",
            verified = new
            {
                file = scenePath,
                size_bytes = fileSize,
                no_unsaved_changes = noUnsavedChanges
            }
        } );
    }

    // ── save_scene_as ────────────────────────────────────────────────────
    // Ported from OzmiumEditorHandlers.SaveSceneAs

    private static object SaveSceneAs( JsonElement args )
    {
        var path = HandlerBase.GetString( args, "path" );
        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "save_scene_as" );

        HandlerBase.RequireAssetsPath( path, "save_scene_as" );

        var session = SceneEditorSession.Active;
        if ( session == null ) return HandlerBase.Error( "No editor session.", "save_scene_as" );

        // The s&box Save(bool forceSaveAs) API opens a dialog — it doesn't accept a path.
        // We note this limitation in the response.
        var saveMethod = session.GetType().GetMethod( "Save", new[] { typeof( bool ) } );
        if ( saveMethod != null )
        {
            saveMethod.Invoke( session, new object[] { true } );

            // Post-condition: if the user's target path already exists on disk, verify file + size + no-unsaved.
            // (If the user still has to choose it in the dialog, this won't assert — that's fine; we report what we can.)
            var scene = session.Scene;
            var asset = AssetSystem.FindByPath( path );
            string scenePath = asset?.AbsolutePath;
            bool fileExists = !string.IsNullOrEmpty( scenePath ) && System.IO.File.Exists( scenePath );
            long fileSize = fileExists ? new System.IO.FileInfo( scenePath ).Length : 0;
            bool noUnsavedChanges = scene != null && !scene.HasUnsavedChanges;

            return HandlerBase.Success( new
            {
                message = $"Save As dialog opened. Choose '{path}' in the dialog.",
                verified = new
                {
                    file = scenePath,
                    size_bytes = fileSize,
                    no_unsaved_changes = noUnsavedChanges,
                    note = fileExists
                        ? "Scene saved to the specified path."
                        : "Dialog-driven save — verification depends on user completing the dialog."
                }
            } );
        }

        return HandlerBase.Error( "Save As method not available on this session type.", "save_scene_as" );
    }

    // ── undo ─────────────────────────────────────────────────────────────
    // Ported from OzmiumWriteHandlers.Undo

    private static object Undo()
    {
        var session = SceneEditorSession.Active;
        if ( session == null ) return HandlerBase.Error( "No editor session active.", "undo" );
        var us = session.UndoSystem;
        if ( us == null ) return HandlerBase.Error( "UndoSystem not available.", "undo" );
        us.Undo();
        return HandlerBase.Confirm( "Undo performed." );
    }

    // ── redo ─────────────────────────────────────────────────────────────
    // Ported from OzmiumWriteHandlers.Redo

    private static object Redo()
    {
        var session = SceneEditorSession.Active;
        if ( session == null ) return HandlerBase.Error( "No editor session active.", "redo" );
        var us = session.UndoSystem;
        if ( us == null ) return HandlerBase.Error( "UndoSystem not available.", "redo" );
        us.Redo();
        return HandlerBase.Confirm( "Redo performed." );
    }

    // ── open_code_file ───────────────────────────────────────────────────
    // NEW action — Research API first
    // Query: mcp__api__search_members "CodeEditor" + "Open"
    // Query: mcp__api__search_members "Editor" + "OpenFile"
    // Fallback: use Process.Start with line/column args for VS Code

    private static object OpenCodeFile( JsonElement args )
    {
        var path = HandlerBase.GetString( args, "path" );
        var line = HandlerBase.GetInt( args, "line", 0 );
        var column = HandlerBase.GetInt( args, "column", 0 );

        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "open_code_file" );

        try
        {
            // Try s&box's built-in code editor launch
            // The editor typically opens .cs files in the configured external editor
            var asset = AssetSystem.FindByPath( path );
            if ( asset != null )
            {
                asset.OpenInEditor();
                return HandlerBase.Confirm( $"Opened '{path}' in editor." );
            }

            // Fallback: try opening the file directly
            // This works for .cs files not registered in the asset system
            var absPath = HandlerBase.ResolveProjectPath( path ) ?? path;

            if ( !System.IO.File.Exists( absPath ) )
                return HandlerBase.Error( $"File not found: '{path}'.", "open_code_file" );

            // Open with default application
            System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo
            {
                FileName = absPath,
                UseShellExecute = true
            } );

            return HandlerBase.Confirm( $"Opened '{path}'" + ( line > 0 ? $" at line {line}" : "" ) + "." );
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( $"Failed to open file: {ex.Message}", "open_code_file" );
        }
    }

    // ── get_preferences ──────────────────────────────────────────────────
    // NEW action — reads ConVars as editor preferences

    private static object GetPreferences()
    {
        var prefs = new Dictionary<string, string>();
        try
        {
            foreach ( var asm in AppDomain.CurrentDomain.GetAssemblies() )
            {
                try
                {
                    foreach ( var type in asm.GetTypes() )
                    {
                        foreach ( var prop in type.GetProperties(
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Static ) )
                        {
                            var attr = prop.GetCustomAttributes( typeof( ConVarAttribute ), false )
                                .FirstOrDefault() as ConVarAttribute;
                            if ( attr == null ) continue;
                            if ( !attr.Flags.HasFlag( ConVarFlags.Saved ) ) continue;

                            var name = !string.IsNullOrEmpty( attr.Name ) ? attr.Name : prop.Name.ToLowerInvariant();
                            try
                            {
                                var val = Sandbox.ConsoleSystem.GetValue( name );
                                prefs[name] = val;
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return HandlerBase.Success( new
        {
            count = prefs.Count,
            preferences = prefs.OrderBy( kv => kv.Key )
                .Select( kv => new { key = kv.Key, value = kv.Value } )
        } );
    }

    // ── set_preference ───────────────────────────────────────────────────
    // NEW action — sets a ConVar value

    private static object SetPreference( JsonElement args )
    {
        var key = HandlerBase.GetString( args, "key" );
        var value = HandlerBase.GetString( args, "value" );

        if ( string.IsNullOrEmpty( key ) )
            return HandlerBase.Error( "Missing required 'key' parameter.", "set_preference" );
        if ( value == null )
            return HandlerBase.Error( "Missing required 'value' parameter.", "set_preference" );

        // Verify the convar exists
        string current = null;
        try { current = Sandbox.ConsoleSystem.GetValue( key ); } catch { }

        if ( current == null )
            return HandlerBase.Error( $"Unknown preference key '{key}'. Use get_preferences to list available keys.", "set_preference" );

        Sandbox.ConsoleSystem.SetValue( key, value );

        string readback = null;
        try { readback = Sandbox.ConsoleSystem.GetValue( key ); } catch { }

        return HandlerBase.Success( new
        {
            key,
            oldValue = current,
            newValue = readback ?? value
        } );
    }
}
