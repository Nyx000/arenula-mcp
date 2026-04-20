// editor/Editor/Handlers/NetworkingHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Sandbox;

namespace Arenula;

/// <summary>
/// networking tool: add_helper, configure_object, get_status.
/// Editor-time multiplayer setup: NetworkHelper, per-object network flags.
/// </summary>
internal static class NetworkingHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "add_helper"       => AddHelper( args ),
                "configure_object" => ConfigureObject( args ),
                "get_status"       => GetStatus( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: add_helper, configure_object, get_status" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── add_helper ──────────────────────────────────────────────────

    private static object AddHelper( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "add_helper" );

        var warnings = new List<object>();

        var id = HandlerBase.GetString( args, "id" );
        GameObject go;

        if ( !string.IsNullOrEmpty( id ) )
        {
            go = SceneHelpers.FindByIdOrThrow( scene, id, "add_helper" );
        }
        else
        {
            go = scene.CreateObject();
            go.Name = HandlerBase.GetString( args, "name" ) ?? "Network Helper";
            var posStr = HandlerBase.GetString( args, "position" );
            if ( posStr != null ) go.WorldPosition = HandlerBase.ParseVector3( posStr );
        }

        var nh = go.Components.Create<NetworkHelper>();
        nh.StartServer = HandlerBase.GetBool( args, "start_server", true );

        var prefabPath = HandlerBase.GetString( args, "player_prefab" );
        if ( !string.IsNullOrEmpty( prefabPath ) )
        {
            var prefabFile = ResourceLibrary.Get<PrefabFile>( prefabPath );
            if ( prefabFile == null )
                return HandlerBase.Error(
                    $"PrefabFile not found at '{prefabPath}'.",
                    "add_helper",
                    "Prefabs must be indexed. User-created prefabs belong under 'Assets/'; path is relative to Assets/." );
            var prefabScene = SceneUtility.GetPrefabScene( prefabFile );
            nh.PlayerPrefab = prefabScene;
        }

        var spawnIds = HandlerBase.GetString( args, "spawn_point_ids" );
        if ( !string.IsNullOrEmpty( spawnIds ) )
        {
            nh.SpawnPoints ??= new List<GameObject>();
            int idx = 0;
            foreach ( var spId in spawnIds.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
            {
                var spGo = SceneHelpers.FindById( scene, spId );
                if ( spGo != null )
                    nh.SpawnPoints.Add( spGo );
                else
                    warnings.Add( HandlerBase.Warning(
                        $"GameObject '{spId}' not found; skipped.",
                        field: $"spawn_point_ids[{idx}]",
                        suggestion: "Verify the GUID via 'scene.find'." ) );
                idx++;
            }
        }

        return HandlerBase.Success( new
        {
            message = $"Added NetworkHelper to '{go.Name}'.",
            id = go.Id.ToString(),
            component_id = nh.Id.ToString(),
            start_server = nh.StartServer,
            spawn_points = nh.SpawnPoints?.Count ?? 0,
            warnings = warnings.Count > 0 ? warnings : null
        } );
    }

    // ── configure_object ────────────────────────────────────────────

    private static object ConfigureObject( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure_object" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_object" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "configure_object" );
        var net = go.Network;

        var flagsStr = HandlerBase.GetString( args, "flags" );
        if ( !string.IsNullOrEmpty( flagsStr ) )
        {
            var flags = NetworkFlags.None;
            foreach ( var f in flagsStr.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
            {
                flags |= f.ToLowerInvariant() switch
                {
                    "no_interpolation"   => NetworkFlags.NoInterpolation,
                    "no_position_sync"   => NetworkFlags.NoPositionSync,
                    "no_rotation_sync"   => NetworkFlags.NoRotationSync,
                    "no_scale_sync"      => NetworkFlags.NoScaleSync,
                    "no_transform_sync"  => NetworkFlags.NoTransformSync,
                    _ => NetworkFlags.None
                };
            }
            net.Flags = flags;
        }

        var ownerTransfer = HandlerBase.GetString( args, "owner_transfer" );
        var otParsed = HandlerBase.ResolveEnum<OwnerTransfer>( ownerTransfer, "owner_transfer", "configure_object" );
        if ( otParsed.HasValue ) net.SetOwnerTransfer( otParsed.Value );

        var orphanedMode = HandlerBase.GetString( args, "orphaned_mode" );
        var noParsed = HandlerBase.ResolveEnum<NetworkOrphaned>( orphanedMode, "orphaned_mode", "configure_object" );
        if ( noParsed.HasValue ) net.SetOrphanedMode( noParsed.Value );

        if ( HandlerBase.TryGetBool( args, "always_transmit", out var at ) ) net.AlwaysTransmit = at;

        if ( HandlerBase.TryGetBool( args, "interpolation", out var int ) ) net.Interpolation = int;

        return HandlerBase.Confirm( $"Configured network settings on '{go.Name}'." );
    }

    // ── get_status ──────────────────────────────────────────────────

    private static object GetStatus( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "get_status" );

        var id = HandlerBase.GetString( args, "id" );

        // Single object query
        if ( !string.IsNullOrEmpty( id ) )
        {
            var go = SceneHelpers.FindByIdOrThrow( scene, id, "get_status" );
            var net = go.Network;
            return HandlerBase.Success( new
            {
                id = go.Id.ToString(),
                name = go.Name,
                is_networked = net.Active,
                flags = net.Flags.ToString(),
                owner_transfer = net.OwnerTransfer.ToString(),
                orphaned_mode = net.NetworkOrphaned.ToString(),
                always_transmit = net.AlwaysTransmit,
                interpolation = net.Interpolation,
                is_owner = net.IsOwner,
                is_proxy = net.IsProxy
            } );
        }

        // Scene-wide scan
        var networked = SceneHelpers.WalkAll( scene )
            .Where( go => go.Network.Active )
            .ToList();

        var offset = HandlerBase.GetInt( args, "offset", 0 );
        var limit = HandlerBase.GetInt( args, "limit", 50 );

        return HandlerBase.Paginate( networked, offset, limit, go => new
        {
            id = go.Id.ToString(),
            name = go.Name,
            flags = go.Network.Flags.ToString(),
            owner_transfer = go.Network.OwnerTransfer.ToString(),
            interpolation = go.Network.Interpolation
        } );
    }
}
