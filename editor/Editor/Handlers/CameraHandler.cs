// Editor/Handlers/CameraHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// camera tool: create, configure, capture_viewport, capture_tour, orbit_capture.
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
                "capture_tour"     => CaptureTour( args ),
                "orbit_capture"    => OrbitCapture( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: create, configure, capture_viewport, capture_tour, orbit_capture" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── create ────────────────────────────────────────────────────────

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
        HandlerBase.ApplyColor( args, "background_color", c => cam.BackgroundColor = c );
        if ( args.TryGetProperty( "priority", out var prEl ) && prEl.ValueKind == JsonValueKind.Number )
            cam.Priority = prEl.GetInt32();

        return HandlerBase.Confirm( $"Configured CameraComponent on '{go.Name}'." );
    }

    // ── capture_tour ──────────────────────────────────────────────────────
    // Render multiple named shots in one call.
    // Args: shots (array of {position, look_at, label?, fov?}), width, height, quality, fov

    private static object CaptureTour( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "capture_tour" );

        if ( !args.TryGetProperty( "shots", out var shotsEl ) || shotsEl.ValueKind != JsonValueKind.Array )
            return HandlerBase.Error( "Parameter 'shots' is required — array of {position, look_at, label?}.", "capture_tour" );

        var width   = Math.Clamp( HandlerBase.GetInt( args, "width",   1280 ), 320,  3840 );
        var height  = Math.Clamp( HandlerBase.GetInt( args, "height",  720  ), 240,  2160 );
        var quality = Math.Clamp( HandlerBase.GetInt( args, "quality", 75   ), 10,   100  );
        var fov     = HandlerBase.GetFloat( args, "fov", 90f );

        var camComp = FindCamera( scene );
        if ( camComp == null )
            return HandlerBase.Error( "No CameraComponent found in scene.", "capture_tour" );

        var pixmap  = new Pixmap( width, height );
        var content = new List<object>();
        var fired   = 0;
        var errors  = new List<string>();

        foreach ( var shot in shotsEl.EnumerateArray() )
        {
            var posStr    = HandlerBase.GetString( shot, "position" );
            var lookAtStr = HandlerBase.GetString( shot, "look_at" );
            var label     = HandlerBase.GetString( shot, "label" ) ?? $"shot {fired + 1}";
            var shotFov   = HandlerBase.GetFloat( shot, "fov", fov );

            if ( string.IsNullOrEmpty( posStr ) || string.IsNullOrEmpty( lookAtStr ) )
            {
                errors.Add( $"'{label}': missing position or look_at — skipped." );
                continue;
            }

            Vector3 pos, lookAt;
            try
            {
                pos    = HandlerBase.ParseVector3( posStr );
                lookAt = HandlerBase.ParseVector3( lookAtStr );
            }
            catch ( Exception ex )
            {
                errors.Add( $"'{label}': parse error — {ex.Message}" );
                continue;
            }

            var ( data, err ) = RenderShot( camComp, pos, lookAt, shotFov, pixmap, quality );
            if ( data == null ) { errors.Add( $"'{label}': {err}" ); continue; }

            content.Add( new { type = "image", data = Convert.ToBase64String( data ), mimeType = "image/jpeg" } );
            content.Add( new { type = "text",  text = $"[{label}]" } );
            fired++;
        }

        if ( fired == 0 )
            return HandlerBase.Error( $"No shots rendered. {string.Join( " ", errors )}", "capture_tour" );

        if ( errors.Count > 0 )
            content.Add( new { type = "text", text = $"Skipped: {string.Join( "; ", errors )}" } );

        content.Add( new { type = "text", text = $"Tour complete — {fired} shots at {width}x{height}." } );
        return new { content };
    }

    // ── orbit_capture ─────────────────────────────────────────────────────
    // Evenly-spaced orbit shots around a target point.
    // Args: target (x,y,z), radius, elevation (world Z of camera), count, fov, width, height, quality, start_angle

    private static object OrbitCapture( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "orbit_capture" );

        var targetStr = HandlerBase.GetString( args, "target" );
        if ( string.IsNullOrEmpty( targetStr ) )
            return HandlerBase.Error( "Parameter 'target' is required (x,y,z world position to orbit).", "orbit_capture" );

        Vector3 target;
        try { target = HandlerBase.ParseVector3( targetStr ); }
        catch ( Exception ex ) { return HandlerBase.Error( $"Invalid target: {ex.Message}", "orbit_capture" ); }

        var radius     = HandlerBase.GetFloat( args, "radius",      300f );
        var elevation  = HandlerBase.GetFloat( args, "elevation",   200f );
        var count      = Math.Clamp( HandlerBase.GetInt( args, "count", 8 ), 2, 32 );
        var fov        = HandlerBase.GetFloat( args, "fov",         75f  );
        var width      = Math.Clamp( HandlerBase.GetInt( args, "width",   1280 ), 320, 3840 );
        var height     = Math.Clamp( HandlerBase.GetInt( args, "height",  720  ), 240, 2160 );
        var quality    = Math.Clamp( HandlerBase.GetInt( args, "quality", 75   ), 10,  100  );
        var startAngle = HandlerBase.GetFloat( args, "start_angle", 0f );

        var camComp = FindCamera( scene );
        if ( camComp == null )
            return HandlerBase.Error( "No CameraComponent found in scene.", "orbit_capture" );

        var pixmap  = new Pixmap( width, height );
        var content = new List<object>();
        var rendered = 0;

        for ( int i = 0; i < count; i++ )
        {
            var angleDeg = ( startAngle + 360f * i / count ) % 360f;
            var angleRad = angleDeg * MathF.PI / 180f;

            var camPos = new Vector3(
                target.x + radius * MathF.Cos( angleRad ),
                target.y + radius * MathF.Sin( angleRad ),
                elevation
            );

            var compass = angleDeg switch
            {
                < 22.5f  => "E",
                < 67.5f  => "NE",
                < 112.5f => "N",
                < 157.5f => "NW",
                < 202.5f => "W",
                < 247.5f => "SW",
                < 292.5f => "S",
                < 337.5f => "SE",
                _        => "E"
            };

            var label = $"orbit {compass} ({angleDeg:F0}°)";
            var ( data, err ) = RenderShot( camComp, camPos, target, fov, pixmap, quality );

            if ( data == null )
            {
                content.Add( new { type = "text", text = $"[{label}] failed: {err}" } );
                continue;
            }

            content.Add( new { type = "image", data = Convert.ToBase64String( data ), mimeType = "image/jpeg" } );
            content.Add( new { type = "text",  text = $"[{label}]" } );
            rendered++;
        }

        content.Add( new { type = "text", text = $"Orbit complete — {rendered}/{count} shots, radius={radius}, elevation={elevation}, target={targetStr}." } );
        return new { content };
    }

    // ── shared helpers ────────────────────────────────────────────────────

    private static CameraComponent FindCamera( Scene scene ) =>
        scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.IsMainCamera && c.Enabled )
        ?? scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.Enabled );

    /// <summary>
    /// Move camComp to pos looking at lookAt, render to pixmap, restore original transform.
    /// Returns (jpeg bytes, null) on success or (null, error message) on failure.
    /// </summary>
    private static ( byte[] data, string error ) RenderShot(
        CameraComponent camComp, Vector3 pos, Vector3 lookAt, float fov, Pixmap pixmap, int quality )
    {
        var go      = camComp.GameObject;
        var origPos = go.WorldPosition;
        var origRot = go.WorldRotation;
        var origFov = camComp.FieldOfView;

        go.WorldPosition = pos;

        var dir    = lookAt - pos;
        var horiz  = new Vector2( dir.x, dir.y );
        var yaw    = MathF.Atan2( horiz.y, horiz.x ) * ( 180f / MathF.PI );
        var pitch  = MathF.Atan2( -dir.z, horiz.Length ) * ( 180f / MathF.PI );
        go.WorldRotation   = Rotation.From( pitch, yaw, 0f );
        camComp.FieldOfView = fov;

        var ok = camComp.RenderToPixmap( pixmap );

        go.WorldPosition   = origPos;
        go.WorldRotation   = origRot;
        camComp.FieldOfView = origFov;

        if ( !ok ) return ( null, "RenderToPixmap failed" );
        var bytes = pixmap.GetJpeg( quality );
        return bytes is { Length: > 0 } ? ( bytes, null ) : ( null, "Failed to encode JPEG" );
    }
}
