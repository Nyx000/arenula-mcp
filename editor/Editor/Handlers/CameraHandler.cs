// Editor/Handlers/CameraHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// camera tool: create, configure.
/// Ported from CameraToolHandlers.cs (149 lines).
/// </summary>
internal static class CameraHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "create"           => Create( args ),
                "configure"        => Configure( args ),
                "capture_viewport" => CaptureViewport( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: create, configure, capture_viewport" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── create ────────────────────────────────────────────────────────
    // Ported from CameraToolHandlers.CreateCamera

    private static object Create( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create" );

        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : new Vector3( 0, 100, 0 );

        var rotStr = HandlerBase.GetString( args, "rotation" );
        var rotation = rotStr != null
            ? Rotation.From( HandlerBase.ParseVector3( rotStr ).x,
                             HandlerBase.ParseVector3( rotStr ).y,
                             HandlerBase.ParseVector3( rotStr ).z )
            : Rotation.From( -90, 0, 0 );

        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Camera";
        go.WorldPosition = position;
        go.WorldRotation = rotation;

        var cam = go.Components.Create<CameraComponent>();
        if ( args.TryGetProperty( "fov", out var fovEl ) && fovEl.ValueKind == JsonValueKind.Number )
            cam.FieldOfView = fovEl.GetSingle();
        else
            cam.FieldOfView = 60f;

        if ( args.TryGetProperty( "near_clip", out var znEl ) && znEl.ValueKind == JsonValueKind.Number )
            cam.ZNear = znEl.GetSingle();
        else
            cam.ZNear = 10f;

        if ( args.TryGetProperty( "far_clip", out var zfEl ) && zfEl.ValueKind == JsonValueKind.Number )
            cam.ZFar = zfEl.GetSingle();
        else
            cam.ZFar = 10000f;

        cam.IsMainCamera = HandlerBase.GetBool( args, "is_main_camera", true );

        if ( args.TryGetProperty( "orthographic", out var orthoEl ) &&
             ( orthoEl.ValueKind == JsonValueKind.True || orthoEl.ValueKind == JsonValueKind.False ) )
            cam.Orthographic = orthoEl.GetBoolean();
        if ( args.TryGetProperty( "orthographic_height", out var ohEl ) && ohEl.ValueKind == JsonValueKind.Number )
            cam.OrthographicHeight = ohEl.GetSingle();

        return HandlerBase.Success( new
        {
            message = $"Created Camera '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition ),
            fov = cam.FieldOfView
        } );
    }

    // ── capture_viewport ────────────────────────────────────────────────

    private static object CaptureViewport( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "capture_viewport" );

        var width = HandlerBase.GetInt( args, "width", 1280 );
        var height = HandlerBase.GetInt( args, "height", 720 );
        var quality = HandlerBase.GetInt( args, "quality", 75 );

        width = Math.Clamp( width, 320, 3840 );
        height = Math.Clamp( height, 240, 2160 );
        quality = Math.Clamp( quality, 10, 100 );

        var posStr = HandlerBase.GetString( args, "position" );
        var rotStr = HandlerBase.GetString( args, "rotation" );
        var lookAtStr = HandlerBase.GetString( args, "look_at" );

        var pixmap = new Pixmap( width, height );

        // If position/rotation/look_at specified, move the scene camera temporarily
        if ( posStr != null || rotStr != null || lookAtStr != null )
        {
            var camComp = scene.GetAllComponents<CameraComponent>()
                .FirstOrDefault( c => c.IsMainCamera && c.Enabled );
            if ( camComp == null )
                camComp = scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.Enabled );
            if ( camComp == null )
                return HandlerBase.Error( "No CameraComponent found in scene.", "capture_viewport" );

            var go = camComp.GameObject;

            // Save original transform
            var origPos = go.WorldPosition;
            var origRot = go.WorldRotation;
            var origFov = camComp.FieldOfView;

            // Move to requested viewpoint
            if ( posStr != null )
                go.WorldPosition = HandlerBase.ParseVector3( posStr );

            // look_at takes priority over rotation — compute pitch/yaw from position to target
            if ( lookAtStr != null )
            {
                var target = HandlerBase.ParseVector3( lookAtStr );
                var dir = ( target - go.WorldPosition );
                var horiz = new Vector2( dir.x, dir.y );
                var yaw = MathF.Atan2( horiz.y, horiz.x ) * ( 180f / MathF.PI );
                var dist = horiz.Length;
                var pitch = MathF.Atan2( -dir.z, dist ) * ( 180f / MathF.PI );
                go.WorldRotation = Rotation.From( pitch, yaw, 0f );
            }
            else if ( rotStr != null )
            {
                var r = HandlerBase.ParseVector3( rotStr );
                go.WorldRotation = Rotation.From( r.x, r.y, r.z );
            }

            camComp.FieldOfView = HandlerBase.GetFloat( args, "fov", 90f );

            var ok = camComp.RenderToPixmap( pixmap );

            // Restore original transform
            go.WorldPosition = origPos;
            go.WorldRotation = origRot;
            camComp.FieldOfView = origFov;

            if ( !ok )
                return HandlerBase.Error( "RenderToPixmap failed.", "capture_viewport" );
        }
        else
        {
            if ( !scene.RenderToPixmap( pixmap ) )
                return HandlerBase.Error( "RenderToPixmap failed — scene may not be ready.", "capture_viewport" );
        }

        var bytes = pixmap.GetJpeg( quality );
        if ( bytes == null || bytes.Length == 0 )
            return HandlerBase.Error( "Failed to encode viewport image.", "capture_viewport" );

        var caption = $"Viewport capture {width}x{height} ({bytes.Length / 1024}KB)";
        if ( posStr != null )
            caption += $" from {posStr}";

        return HandlerBase.Image( bytes, "image/jpeg", caption );
    }

    // ── configure ─────────────────────────────────────────────────────
    // Ported from CameraToolHandlers.ConfigureCamera

    private static object Configure( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "configure" );
        var cam = go.Components.Get<CameraComponent>();
        if ( cam == null )
            return HandlerBase.Error( $"No CameraComponent found on '{go.Name}'.", "configure" );

        if ( args.TryGetProperty( "fov", out var fovEl ) && fovEl.ValueKind == JsonValueKind.Number )
            cam.FieldOfView = fovEl.GetSingle();
        if ( args.TryGetProperty( "near_clip", out var znEl ) && znEl.ValueKind == JsonValueKind.Number )
            cam.ZNear = znEl.GetSingle();
        if ( args.TryGetProperty( "far_clip", out var zfEl ) && zfEl.ValueKind == JsonValueKind.Number )
            cam.ZFar = zfEl.GetSingle();
        if ( args.TryGetProperty( "is_main_camera", out var mcEl ) &&
             ( mcEl.ValueKind == JsonValueKind.True || mcEl.ValueKind == JsonValueKind.False ) )
            cam.IsMainCamera = mcEl.GetBoolean();
        if ( args.TryGetProperty( "orthographic", out var orthoEl ) &&
             ( orthoEl.ValueKind == JsonValueKind.True || orthoEl.ValueKind == JsonValueKind.False ) )
            cam.Orthographic = orthoEl.GetBoolean();
        if ( args.TryGetProperty( "orthographic_height", out var ohEl ) && ohEl.ValueKind == JsonValueKind.Number )
            cam.OrthographicHeight = ohEl.GetSingle();
        var bgStr = HandlerBase.GetString( args, "background_color" );
        if ( !string.IsNullOrEmpty( bgStr ) )
        {
            try { cam.BackgroundColor = Color.Parse( bgStr ) ?? default; } catch { }
        }
        if ( args.TryGetProperty( "priority", out var prEl ) && prEl.ValueKind == JsonValueKind.Number )
            cam.Priority = prEl.GetInt32();

        return HandlerBase.Confirm( $"Configured CameraComponent on '{go.Name}'." );
    }
}
