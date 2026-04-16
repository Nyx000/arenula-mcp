// editor/Editor/Handlers/PlayerHandler.cs
using System;
using System.Text.Json;
using Sandbox;

namespace Arenula;

/// <summary>
/// player tool: create, configure_movement, configure_camera, configure_body, configure_interaction.
/// Curated access to PlayerController's 72 properties, grouped by concern.
/// </summary>
internal static class PlayerHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "create"                  => Create( args ),
                "configure_movement"      => ConfigureMovement( args ),
                "configure_camera"        => ConfigureCamera( args ),
                "configure_body"          => ConfigureBody( args ),
                "configure_interaction"   => ConfigureInteraction( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: create, configure_movement, configure_camera, configure_body, configure_interaction" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── Shared: find PlayerController on a GO ────────────────────────

    private static PlayerController FindController( Scene scene, string id, string action, out object error )
    {
        error = null;
        var go = SceneHelpers.FindByIdOrThrow( scene, id, action );
        var pc = go.Components.Get<PlayerController>();
        if ( pc == null )
            error = HandlerBase.Error( $"No PlayerController found on '{go.Name}'.", action,
                "Add a PlayerController first with player.create." );
        return pc;
    }

    // ── create ───────────────────────────────────────────────────────

    private static object Create( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create" );

        var id = HandlerBase.GetString( args, "id" );
        GameObject go;

        if ( !string.IsNullOrEmpty( id ) )
        {
            go = SceneHelpers.FindByIdOrThrow( scene, id, "create" );
        }
        else
        {
            go = scene.CreateObject();
            go.Name = HandlerBase.GetString( args, "name" ) ?? "Player";
            var posStr = HandlerBase.GetString( args, "position" );
            if ( posStr != null ) go.WorldPosition = HandlerBase.ParseVector3( posStr );
        }

        var pc = go.Components.Create<PlayerController>();

        var bodyModel = HandlerBase.GetString( args, "body_model" );
        if ( !string.IsNullOrEmpty( bodyModel ) )
        {
            pc.CreateBodyRenderer();
            if ( pc.Renderer != null )
            {
                var model = Model.Load( bodyModel );
                if ( model != null ) pc.Renderer.Model = model;
            }
        }

        return HandlerBase.Success( new
        {
            message = $"Created PlayerController on '{go.Name}'.",
            id = go.Id.ToString(),
            component_id = pc.Id.ToString(),
            body_height = pc.BodyHeight,
            body_radius = pc.BodyRadius
        } );
    }

    // ── configure_movement ──────────────────────────────────────────

    private static object ConfigureMovement( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure_movement" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_movement" );

        var pc = FindController( scene, id, "configure_movement", out var error );
        if ( error != null ) return error;

        if ( args.TryGetProperty( "walk_speed", out var wsEl ) && wsEl.ValueKind == JsonValueKind.Number )
            pc.WalkSpeed = wsEl.GetSingle();
        if ( args.TryGetProperty( "run_speed", out var rsEl ) && rsEl.ValueKind == JsonValueKind.Number )
            pc.RunSpeed = rsEl.GetSingle();
        if ( args.TryGetProperty( "ducked_speed", out var dsEl ) && dsEl.ValueKind == JsonValueKind.Number )
            pc.DuckedSpeed = dsEl.GetSingle();
        if ( args.TryGetProperty( "jump_speed", out var jsEl ) && jsEl.ValueKind == JsonValueKind.Number )
            pc.JumpSpeed = jsEl.GetSingle();
        if ( args.TryGetProperty( "acceleration_time", out var atEl ) && atEl.ValueKind == JsonValueKind.Number )
            pc.AccelerationTime = atEl.GetSingle();
        if ( args.TryGetProperty( "deacceleration_time", out var dtEl ) && dtEl.ValueKind == JsonValueKind.Number )
            pc.DeaccelerationTime = dtEl.GetSingle();
        if ( args.TryGetProperty( "air_friction", out var afEl ) && afEl.ValueKind == JsonValueKind.Number )
            pc.AirFriction = afEl.GetSingle();
        if ( args.TryGetProperty( "brake_power", out var bpEl ) && bpEl.ValueKind == JsonValueKind.Number )
            pc.BrakePower = bpEl.GetSingle();
        if ( args.TryGetProperty( "run_by_default", out var rbdEl ) &&
             ( rbdEl.ValueKind == JsonValueKind.True || rbdEl.ValueKind == JsonValueKind.False ) )
            pc.RunByDefault = rbdEl.GetBoolean();

        return HandlerBase.Confirm( $"Configured movement on '{pc.GameObject.Name}': walk={pc.WalkSpeed}, run={pc.RunSpeed}, jump={pc.JumpSpeed}." );
    }

    // ── configure_camera ────────────────────────────────────────────

    private static object ConfigureCamera( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure_camera" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_camera" );

        var pc = FindController( scene, id, "configure_camera", out var error );
        if ( error != null ) return error;

        if ( args.TryGetProperty( "third_person", out var tpEl ) &&
             ( tpEl.ValueKind == JsonValueKind.True || tpEl.ValueKind == JsonValueKind.False ) )
            pc.ThirdPerson = tpEl.GetBoolean();
        if ( args.TryGetProperty( "hide_body_in_first_person", out var hbEl ) &&
             ( hbEl.ValueKind == JsonValueKind.True || hbEl.ValueKind == JsonValueKind.False ) )
            pc.HideBodyInFirstPerson = hbEl.GetBoolean();
        var offsetStr = HandlerBase.GetString( args, "camera_offset" );
        if ( offsetStr != null )
            pc.CameraOffset = HandlerBase.ParseVector3( offsetStr );
        if ( args.TryGetProperty( "eye_distance_from_top", out var edEl ) && edEl.ValueKind == JsonValueKind.Number )
            pc.EyeDistanceFromTop = edEl.GetSingle();
        if ( args.TryGetProperty( "pitch_clamp", out var pcEl ) && pcEl.ValueKind == JsonValueKind.Number )
            pc.PitchClamp = pcEl.GetSingle();
        if ( args.TryGetProperty( "look_sensitivity", out var lsEl ) && lsEl.ValueKind == JsonValueKind.Number )
            pc.LookSensitivity = lsEl.GetSingle();
        if ( args.TryGetProperty( "use_fov_from_preferences", out var ufEl ) &&
             ( ufEl.ValueKind == JsonValueKind.True || ufEl.ValueKind == JsonValueKind.False ) )
            pc.UseFovFromPreferences = ufEl.GetBoolean();

        return HandlerBase.Confirm( $"Configured camera on '{pc.GameObject.Name}': third_person={pc.ThirdPerson}." );
    }

    // ── configure_body ──────────────────────────────────────────────

    private static object ConfigureBody( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure_body" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_body" );

        var pc = FindController( scene, id, "configure_body", out var error );
        if ( error != null ) return error;

        if ( args.TryGetProperty( "body_height", out var bhEl ) && bhEl.ValueKind == JsonValueKind.Number )
            pc.BodyHeight = bhEl.GetSingle();
        if ( args.TryGetProperty( "body_radius", out var brEl ) && brEl.ValueKind == JsonValueKind.Number )
            pc.BodyRadius = brEl.GetSingle();
        if ( args.TryGetProperty( "body_mass", out var bmEl ) && bmEl.ValueKind == JsonValueKind.Number )
            pc.BodyMass = bmEl.GetSingle();
        if ( args.TryGetProperty( "ducked_height", out var dhEl ) && dhEl.ValueKind == JsonValueKind.Number )
            pc.DuckedHeight = dhEl.GetSingle();

        var collisionTags = HandlerBase.GetString( args, "collision_tags" );
        if ( !string.IsNullOrEmpty( collisionTags ) )
        {
            pc.BodyCollisionTags.RemoveAll();
            foreach ( var tag in collisionTags.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
                pc.BodyCollisionTags.Add( tag );
        }

        return HandlerBase.Confirm( $"Configured body on '{pc.GameObject.Name}': height={pc.BodyHeight}, radius={pc.BodyRadius}, mass={pc.BodyMass}." );
    }

    // ── configure_interaction ───────────────────────────────────────

    private static object ConfigureInteraction( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure_interaction" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_interaction" );

        var pc = FindController( scene, id, "configure_interaction", out var error );
        if ( error != null ) return error;

        if ( args.TryGetProperty( "enable_pressing", out var epEl ) &&
             ( epEl.ValueKind == JsonValueKind.True || epEl.ValueKind == JsonValueKind.False ) )
            pc.EnablePressing = epEl.GetBoolean();
        var useButton = HandlerBase.GetString( args, "use_button" );
        if ( useButton != null )
            pc.UseButton = useButton;
        if ( args.TryGetProperty( "reach_length", out var rlEl ) && rlEl.ValueKind == JsonValueKind.Number )
            pc.ReachLength = rlEl.GetSingle();

        return HandlerBase.Confirm( $"Configured interaction on '{pc.GameObject.Name}': pressing={pc.EnablePressing}, reach={pc.ReachLength}." );
    }
}
