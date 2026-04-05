using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// session tool: list, set_active, load_scene.
/// Manages editor sessions (scene/prefab tabs).
/// Ported from Ozmium SessionToolHandlers.
/// </summary>
internal static class SessionHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "list"       => List(),
                "set_active" => SetActive( args ),
                "load_scene" => LoadScene( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: list, set_active, load_scene" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── list ──────────────────────────────────────────────────────────────
    // Ported from SessionToolHandlers.GetEditorSessions

    private static object List()
    {
        var sessions = SceneEditorSession.All ?? new List<SceneEditorSession>();
        var active = SceneEditorSession.Active;
        var results = new List<object>();

        for ( int i = 0; i < sessions.Count; i++ )
        {
            var s = sessions[i];
            results.Add( new
            {
                index = i,
                name = s.Scene?.Name ?? "(unnamed)",
                isActive = s == active,
                isPrefabSession = s.IsPrefabSession,
                hasUnsavedChanges = s.HasUnsavedChanges
            } );
        }

        return HandlerBase.Success( new
        {
            count = results.Count,
            sessions = results
        } );
    }

    // ── set_active ────────────────────────────────────────────────────────
    // Ported from SessionToolHandlers.SetActiveSession

    private static object SetActive( JsonElement args )
    {
        var sessionId = HandlerBase.GetString( args, "session_id" );
        if ( string.IsNullOrEmpty( sessionId ) )
            return HandlerBase.Error( "Missing required 'session_id' parameter.", "set_active",
                "Provide the session name or index from session.list." );

        var sessions = SceneEditorSession.All ?? new List<SceneEditorSession>();
        if ( sessions.Count == 0 )
            return HandlerBase.Error( "No editor sessions open.", "set_active" );

        SceneEditorSession target = null;

        // Try parsing as index first
        if ( int.TryParse( sessionId, out var index ) )
        {
            if ( index >= 0 && index < sessions.Count )
                target = sessions[index];
            else
                return HandlerBase.Error(
                    $"Index {index} out of range. {sessions.Count} sessions available (0-{sessions.Count - 1}).",
                    "set_active" );
        }
        else
        {
            // Try matching by name (partial, case-insensitive)
            target = sessions.FirstOrDefault( s =>
                s.Scene?.Name?.Contains( sessionId, StringComparison.OrdinalIgnoreCase ) == true );
            if ( target == null )
                return HandlerBase.Error(
                    $"No session found matching '{sessionId}'. Use session.list to see available sessions.",
                    "set_active" );
        }

        target.MakeActive();
        return HandlerBase.Confirm( $"Activated session: '{target.Scene?.Name ?? "(unnamed)"}'." );
    }

    // ── load_scene ────────────────────────────────────────────────────────
    // Ported from SessionToolHandlers.LoadScene

    private static object LoadScene( JsonElement args )
    {
        var path = HandlerBase.GetString( args, "path" );
        if ( string.IsNullOrEmpty( path ) )
            return HandlerBase.Error( "Missing required 'path' parameter.", "load_scene" );

        // CreateFromPath handles both .scene and .prefab files.
        // It reuses an existing session if the file is already open.
        var session = SceneEditorSession.CreateFromPath( path );

        if ( session == null )
        {
            // Try with common path variations
            if ( !path.EndsWith( ".scene" ) && !path.EndsWith( ".prefab" ) )
                session = SceneEditorSession.CreateFromPath( path + ".scene" );

            if ( session == null )
                return HandlerBase.Error(
                    $"Could not open '{path}'. Ensure the file exists and is a .scene or .prefab.",
                    "load_scene",
                    "Use asset_query.search with type 'scene' to list available scenes." );
        }

        session.MakeActive();

        return HandlerBase.Success( new
        {
            message = $"Opened and activated: '{session.Scene?.Name ?? path}'.",
            name = session.Scene?.Name,
            isPrefabSession = session.IsPrefabSession
        } );
    }
}
