// Editor/Handlers/AudioHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;
using Sandbox.Audio;

namespace Arenula;

/// <summary>
/// audio tool: create, configure.
/// Consolidates 5 Ozmium create tools into 1 create action with type param.
/// Types: point, soundscape, box, dsp_volume, listener.
/// </summary>
internal static class AudioHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "create"    => Create( args ),
                "configure" => Configure( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: create, configure" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── create ────────────────────────────────────────────────────────
    // Consolidates: CreateSoundPoint, CreateSoundscapeTrigger, CreateSoundBox,
    //               CreateDspVolume, CreateAudioListener

    private static object Create( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create" );

        var type = HandlerBase.GetString( args, "type", "point" );
        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;

        return type.ToLowerInvariant() switch
        {
            "point"      => CreateSoundPoint( scene, args, position ),
            "soundscape" => CreateSoundscape( scene, args, position ),
            "box"        => CreateSoundBox( scene, args, position ),
            "dsp_volume" => CreateDspVolume( scene, args, position ),
            "listener"   => CreateAudioListener( scene, args, position ),
            _ => HandlerBase.Error(
                $"Unknown audio type '{type}'.",
                "create",
                "Valid types: point, soundscape, box, dsp_volume, listener" )
        };
    }

    private static object CreateSoundPoint( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Sound Point";
        go.WorldPosition = position;

        var snd = go.Components.Create<SoundPointComponent>();

        var soundEvent = HandlerBase.GetString( args, "sound_event" );
        if ( !string.IsNullOrEmpty( soundEvent ) )
            snd.SoundEvent = HandlerBase.RequireResource<SoundEvent>( soundEvent, "create_audio_source" );

        if ( HandlerBase.TryGetFloat( args, "volume", out var v ) ) snd.Volume = v;
        if ( HandlerBase.TryGetFloat( args, "pitch", out var p ) ) snd.Pitch = p;
        snd.PlayOnStart = HandlerBase.GetBool( args, "play_on_start", true );
        snd.Repeat = HandlerBase.GetBool( args, "repeat", false );

        return HandlerBase.Success( new
        {
            message = $"Created SoundPoint '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    private static object CreateSoundscape( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Soundscape Trigger";
        go.WorldPosition = position;

        var st = go.Components.Create<SoundscapeTrigger>();

        var triggerType = HandlerBase.GetString( args, "trigger_type", "Sphere" );
        var ttParsed = HandlerBase.ResolveEnum<SoundscapeTrigger.TriggerType>( triggerType, "trigger_type", "create_soundscape_trigger" );
        if ( ttParsed.HasValue ) st.Type = ttParsed.Value;

        if ( HandlerBase.TryGetFloat( args, "volume", out var v ) ) st.Volume = v;
        if ( HandlerBase.TryGetFloat( args, "radius", out var r ) ) st.Radius = r;
        st.StayActiveOnExit = HandlerBase.GetBool( args, "stay_active_on_exit", true );

        var boxSizeStr = HandlerBase.GetString( args, "box_size" );
        if ( boxSizeStr != null )
            st.BoxSize = HandlerBase.ParseVector3( boxSizeStr );

        var scapePath = HandlerBase.GetString( args, "soundscape_path" );
        if ( !string.IsNullOrEmpty( scapePath ) )
            st.Soundscape = HandlerBase.RequireResource<Soundscape>( scapePath, "create_soundscape_trigger" );

        return HandlerBase.Success( new
        {
            message = $"Created SoundscapeTrigger '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition ),
            trigger_type = st.Type.ToString()
        } );
    }

    private static object CreateSoundBox( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Sound Box";
        go.WorldPosition = position;

        var sb = go.Components.Create<SoundBoxComponent>();
        if ( HandlerBase.TryGetFloat( args, "volume", out var v ) ) sb.Volume = v;
        sb.PlayOnStart = HandlerBase.GetBool( args, "play_on_start", true );
        sb.Repeat = HandlerBase.GetBool( args, "repeat", false );

        var boxSizeStr = HandlerBase.GetString( args, "box_size" );
        if ( boxSizeStr != null )
            sb.Scale = HandlerBase.ParseVector3( boxSizeStr );

        var soundEvent = HandlerBase.GetString( args, "sound_event" );
        if ( !string.IsNullOrEmpty( soundEvent ) )
            sb.SoundEvent = HandlerBase.RequireResource<SoundEvent>( soundEvent, "create_sound_box" );

        return HandlerBase.Success( new
        {
            message = $"Created SoundBox '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    private static object CreateDspVolume( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "DSP Volume";
        go.WorldPosition = position;

        var dsp = go.Components.Create<DspVolume>();
        dsp.Priority = HandlerBase.GetInt( args, "priority", 0 );

        var targetMixer = HandlerBase.GetString( args, "target_mixer", "Game" );
        if ( !string.IsNullOrEmpty( targetMixer ) )
            dsp.TargetMixer = new MixerHandle { Name = targetMixer };

        var sv = dsp.SceneVolume;
        var volumeType = HandlerBase.GetString( args, "volume_type", "Box" );
        var vtParsed = HandlerBase.ResolveEnum<Sandbox.Volumes.SceneVolume.VolumeTypes>( volumeType, "volume_type", "create_volume" );
        if ( vtParsed.HasValue ) sv.Type = vtParsed.Value;

        if ( sv.Type == Sandbox.Volumes.SceneVolume.VolumeTypes.Box )
        {
            var boxSizeStr = HandlerBase.GetString( args, "box_size" );
            if ( boxSizeStr != null )
            {
                var bsv = HandlerBase.ParseVector3( boxSizeStr );
                sv.Box = BBox.FromPositionAndSize( 0, bsv );
            }
        }
        else if ( sv.Type == Sandbox.Volumes.SceneVolume.VolumeTypes.Sphere )
        {
            if ( args.TryGetProperty( "radius", out var rEl ) && rEl.ValueKind == JsonValueKind.Number )
                sv.Sphere = new Sphere( 0, rEl.GetSingle() );
        }
        dsp.SceneVolume = sv;

        var dspPreset = HandlerBase.GetString( args, "dsp_preset" );
        if ( !string.IsNullOrEmpty( dspPreset ) )
            dsp.Dsp = new DspPresetHandle { Name = dspPreset };

        return HandlerBase.Success( new
        {
            message = $"Created DspVolume '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition ),
            volume_type = sv.Type.ToString()
        } );
    }

    private static object CreateAudioListener( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Audio Listener";
        go.WorldPosition = position;

        var listener = go.Components.Create<AudioListener>();
        listener.UseCameraDirection = HandlerBase.GetBool( args, "use_camera_direction", true );

        return HandlerBase.Success( new
        {
            message = $"Created AudioListener '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
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

        // Find sound component — prefer component_id if given
        BaseSoundComponent snd;
        var compId = HandlerBase.GetString( args, "component_id" );
        if ( !string.IsNullOrEmpty( compId ) && Guid.TryParse( compId, out var cGuid ) )
        {
            snd = go.Components.GetAll().FirstOrDefault( c => c is BaseSoundComponent && c.Id == cGuid ) as BaseSoundComponent;
            if ( snd == null )
                return HandlerBase.Error( $"Component '{compId}' is not a BaseSoundComponent on '{go.Name}'.", "configure" );
        }
        else
        {
            snd = go.Components.GetAll().FirstOrDefault( c => c is BaseSoundComponent ) as BaseSoundComponent;
            if ( snd == null )
                return HandlerBase.Error( $"No sound component found on '{go.Name}'.", "configure" );
        }

        var soundEvent = HandlerBase.GetString( args, "sound_event" );
        if ( !string.IsNullOrEmpty( soundEvent ) )
            snd.SoundEvent = HandlerBase.RequireResource<SoundEvent>( soundEvent, "configure_audio_source" );

        if ( HandlerBase.TryGetFloat( args, "volume", out var v ) ) snd.Volume = v;
        if ( HandlerBase.TryGetFloat( args, "pitch", out var p ) ) snd.Pitch = p;
        if ( HandlerBase.TryGetBool( args, "play_on_start", out var pos ) ) snd.PlayOnStart = pos;
        if ( HandlerBase.TryGetBool( args, "repeat", out var rep ) ) snd.Repeat = rep;
        if ( HandlerBase.TryGetBool( args, "distance_attenuation", out var da ) ) snd.DistanceAttenuation = da;
        if ( HandlerBase.TryGetFloat( args, "distance", out var d ) ) snd.Distance = d;

        return HandlerBase.Confirm( $"Configured sound on '{go.Name}' ({snd.GetType().Name})." );
    }
}
