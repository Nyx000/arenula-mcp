using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;
using InsideGeometryBehaviorType = Sandbox.IndirectLightVolume.InsideGeometryBehavior;

namespace Arenula;

/// <summary>
/// lighting tool: create, configure, create_skybox, set_skybox.
/// Consolidates Ozmium CreateLight + CreateAmbientLight + CreateIndirectLightVolume
/// into a single 'create' action with type param.
/// Ported from LightingToolHandlers.cs (480 lines).
/// </summary>
internal static class LightingHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "create"        => Create( args ),
                "configure"     => Configure( args ),
                "create_skybox" => CreateSkybox( args ),
                "set_skybox"    => SetSkybox( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: create, configure, create_skybox, set_skybox" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── create ────────────────────────────────────────────────────────────
    // Consolidates: CreateLight, CreateAmbientLight, CreateIndirectLightVolume, CreateEnvironmentLight

    private static object Create( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create" );

        var type = HandlerBase.GetString( args, "type", "point" );
        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;

        switch ( type.ToLowerInvariant() )
        {
            case "point":
            case "pointlight":
                return CreateDirectionalOrPointOrSpot( scene, args, position, "point" );
            case "spot":
            case "spotlight":
                return CreateDirectionalOrPointOrSpot( scene, args, position, "spot" );
            case "directional":
            case "directionallight":
                return CreateDirectionalOrPointOrSpot( scene, args, position, "directional" );
            case "ambient":
            case "ambientlight":
                return CreateAmbient( scene, args, position );
            case "environment":
            case "environmentlight":
                return CreateEnvironment( scene, args, position );
            case "indirect_volume":
            case "indirectlightvolume":
                return CreateIndirectVolume( scene, args, position );
            default:
                return HandlerBase.Error(
                    $"Unknown light type '{type}'.",
                    "create",
                    "Valid types: point, spot, directional, ambient, environment, indirect_volume" );
        }
    }

    private static object CreateDirectionalOrPointOrSpot( Scene scene, JsonElement args, Vector3 position, string kind )
    {
        var go = scene.CreateObject();
        go.WorldPosition = position;

        var rotStr = HandlerBase.GetString( args, "rotation" );
        if ( rotStr != null )
        {
            var rv = HandlerBase.ParseVector3( rotStr );
            go.WorldRotation = Rotation.From( rv.x, rv.y, rv.z );
        }

        Component light;
        switch ( kind )
        {
            case "spot":
                light = go.Components.Create<SpotLight>();
                go.Name = HandlerBase.GetString( args, "name" ) ?? "Spot Light";
                break;
            case "directional":
                light = go.Components.Create<DirectionalLight>();
                go.Name = HandlerBase.GetString( args, "name" ) ?? "Directional Light";
                break;
            default: // point
                light = go.Components.Create<PointLight>();
                go.Name = HandlerBase.GetString( args, "name" ) ?? "Point Light";
                break;
        }

        // Apply optional color
        var colorStr = HandlerBase.GetString( args, "color" );
        if ( !string.IsNullOrEmpty( colorStr ) )
        {
            try
            {
                var color = Color.Parse( colorStr ) ?? default;
                var prop = light.GetType().GetProperty( "LightColor" );
                prop?.SetValue( light, color );
            }
            catch { }
        }

        // Apply optional shadows
        if ( args.TryGetProperty( "shadows", out var shEl ) &&
             ( shEl.ValueKind == JsonValueKind.True || shEl.ValueKind == JsonValueKind.False ) )
        {
            var prop = light.GetType().GetProperty( "Shadows" );
            prop?.SetValue( light, shEl.GetBoolean() );
        }

        // Apply intensity via reflection (works for all light types that have it)
        if ( args.TryGetProperty( "intensity", out var intEl ) && intEl.ValueKind == JsonValueKind.Number )
        {
            var prop = light.GetType().GetProperty( "Intensity" );
            if ( prop != null ) prop.SetValue( light, intEl.GetSingle() );
        }

        // Type-specific properties
        if ( light is PointLight pl )
        {
            if ( args.TryGetProperty( "range", out var rangeEl ) && rangeEl.ValueKind == JsonValueKind.Number )
                pl.Radius = rangeEl.GetSingle();
            else if ( args.TryGetProperty( "radius", out var radEl ) && radEl.ValueKind == JsonValueKind.Number )
                pl.Radius = radEl.GetSingle();
            if ( args.TryGetProperty( "attenuation", out var attEl ) && attEl.ValueKind == JsonValueKind.Number )
                pl.Attenuation = attEl.GetSingle();
        }
        else if ( light is SpotLight sl )
        {
            if ( args.TryGetProperty( "range", out var rangeEl ) && rangeEl.ValueKind == JsonValueKind.Number )
                sl.Radius = rangeEl.GetSingle();
            else if ( args.TryGetProperty( "radius", out var radEl ) && radEl.ValueKind == JsonValueKind.Number )
                sl.Radius = radEl.GetSingle();
            if ( args.TryGetProperty( "attenuation", out var attEl ) && attEl.ValueKind == JsonValueKind.Number )
                sl.Attenuation = attEl.GetSingle();
            if ( args.TryGetProperty( "cone_outer", out var coEl ) && coEl.ValueKind == JsonValueKind.Number )
                sl.ConeOuter = coEl.GetSingle();
            if ( args.TryGetProperty( "cone_inner", out var ciEl ) && ciEl.ValueKind == JsonValueKind.Number )
                sl.ConeInner = ciEl.GetSingle();
        }

        return HandlerBase.Success( new
        {
            message = $"Created {kind} light '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    private static object CreateAmbient( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Ambient Light";
        go.WorldPosition = position;

        var amb = go.Components.Create<AmbientLight>();
        var colorStr = HandlerBase.GetString( args, "color", "Gray" );
        try { amb.Color = Color.Parse( colorStr ) ?? default; } catch { }

        return HandlerBase.Success( new
        {
            message = $"Created AmbientLight '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    private static object CreateEnvironment( Scene scene, JsonElement args, Vector3 position )
    {
        // Creates a complete environment setup: DirectionalLight + AmbientLight + SkyBox2D
        var sunColor = HandlerBase.GetString( args, "sun_color", "#FFFFFF" );
        var ambColor = HandlerBase.GetString( args, "ambient_color", "#808080" );
        var skyMat = HandlerBase.GetString( args, "material", "materials/skybox/skybox_day_01.vmat" );

        var rotStr = HandlerBase.GetString( args, "rotation" );
        Rotation sunRot;
        if ( rotStr != null )
        {
            var rv = HandlerBase.ParseVector3( rotStr );
            sunRot = Rotation.From( rv.x, rv.y, rv.z );
        }
        else
        {
            sunRot = Rotation.From( 0, -45, 0 );
        }

        // Create Directional Light (sun)
        var sun = scene.CreateObject();
        sun.Name = "Sun";
        sun.WorldRotation = sunRot;
        var dl = sun.Components.Create<DirectionalLight>();
        try { dl.LightColor = Color.Parse( sunColor ) ?? default; } catch { }

        // Create Ambient Light
        var amb = scene.CreateObject();
        amb.Name = "Ambient Light";
        var ambComp = amb.Components.Create<AmbientLight>();
        try { ambComp.Color = Color.Parse( ambColor ) ?? default; } catch { }

        // Create SkyBox2D
        var sky = scene.CreateObject();
        sky.Name = "Sky Box";
        var skyComp = sky.Components.Create<SkyBox2D>();
        var mat = Material.Load( skyMat );
        if ( mat != null ) skyComp.SkyMaterial = mat;
        skyComp.SkyIndirectLighting = true;

        return HandlerBase.Success( new
        {
            message = "Created environment setup (DirectionalLight + AmbientLight + SkyBox2D).",
            sun = new { id = sun.Id.ToString(), name = sun.Name },
            ambient = new { id = amb.Id.ToString(), name = amb.Name },
            skybox = new { id = sky.Id.ToString(), name = sky.Name }
        } );
    }

    private static object CreateIndirectVolume( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Indirect Light Volume";
        go.WorldPosition = position;

        var ilg = go.Components.Create<IndirectLightVolume>();

        // Size via "size" param as "x,y,z" string
        var sizeStr = HandlerBase.GetString( args, "size" );
        if ( sizeStr != null )
        {
            var sv = HandlerBase.ParseVector3( sizeStr );
            ilg.Bounds = BBox.FromPositionAndSize( 0, sv );
        }

        if ( args.TryGetProperty( "probe_density", out var pdEl ) && pdEl.ValueKind == JsonValueKind.Number )
            ilg.ProbeDensity = pdEl.GetInt32();
        if ( args.TryGetProperty( "normal_bias", out var nbEl ) && nbEl.ValueKind == JsonValueKind.Number )
            ilg.NormalBias = nbEl.GetSingle();
        if ( args.TryGetProperty( "contrast", out var ctEl ) && ctEl.ValueKind == JsonValueKind.Number )
            ilg.Contrast = ctEl.GetSingle();

        var igbStr = HandlerBase.GetString( args, "inside_geometry_behavior" );
        if ( igbStr != null && Enum.TryParse<InsideGeometryBehaviorType>( igbStr, true, out var igb ) )
        {
            var behaviorProp = typeof( IndirectLightVolume ).GetProperties()
                .FirstOrDefault( p => p.PropertyType == typeof( InsideGeometryBehaviorType ) );
            behaviorProp?.SetValue( ilg, igb );
        }

        return HandlerBase.Success( new
        {
            message = $"Created IndirectLightVolume '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition ),
            probe_density = ilg.ProbeDensity
        } );
    }

    // ── configure ─────────────────────────────────────────────────────────
    // Ported from LightingToolHandlers.ConfigureLight

    private static object Configure( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "configure" );
        var light = go.Components.GetAll().FirstOrDefault( c => c is Light ) as Light;
        if ( light == null )
            return HandlerBase.Error( $"No Light component found on '{go.Name}'.", "configure" );

        var colorStr = HandlerBase.GetString( args, "color" );
        if ( !string.IsNullOrEmpty( colorStr ) )
        {
            try
            {
                var c = Color.Parse( colorStr ) ?? default;
                var p = light.GetType().GetProperty( "LightColor" );
                p?.SetValue( light, c );
            }
            catch { }
        }

        if ( args.TryGetProperty( "shadows", out var shEl ) &&
             ( shEl.ValueKind == JsonValueKind.True || shEl.ValueKind == JsonValueKind.False ) )
        {
            var p = light.GetType().GetProperty( "Shadows" );
            p?.SetValue( light, shEl.GetBoolean() );
        }

        if ( args.TryGetProperty( "intensity", out var intEl ) && intEl.ValueKind == JsonValueKind.Number )
        {
            var p = light.GetType().GetProperty( "Intensity" );
            if ( p != null ) p.SetValue( light, intEl.GetSingle() );
        }

        if ( args.TryGetProperty( "range", out var rangeEl ) && rangeEl.ValueKind == JsonValueKind.Number )
        {
            var p = light.GetType().GetProperty( "Radius" );
            if ( p != null ) p.SetValue( light, rangeEl.GetSingle() );
        }
        else if ( args.TryGetProperty( "radius", out var radEl ) && radEl.ValueKind == JsonValueKind.Number )
        {
            var p = light.GetType().GetProperty( "Radius" );
            if ( p != null ) p.SetValue( light, radEl.GetSingle() );
        }

        if ( args.TryGetProperty( "attenuation", out var attEl ) && attEl.ValueKind == JsonValueKind.Number )
        {
            var p = light.GetType().GetProperty( "Attenuation" );
            if ( p != null ) p.SetValue( light, attEl.GetSingle() );
        }

        if ( args.TryGetProperty( "cone_outer", out var coEl ) && coEl.ValueKind == JsonValueKind.Number )
        {
            var p = light.GetType().GetProperty( "ConeOuter" );
            if ( p != null ) p.SetValue( light, coEl.GetSingle() );
        }

        if ( args.TryGetProperty( "cone_inner", out var ciEl ) && ciEl.ValueKind == JsonValueKind.Number )
        {
            var p = light.GetType().GetProperty( "ConeInner" );
            if ( p != null ) p.SetValue( light, ciEl.GetSingle() );
        }

        var fogModeStr = HandlerBase.GetString( args, "fog_mode" );
        if ( fogModeStr != null )
        {
            var p = light.GetType().GetProperty( "FogMode" );
            if ( p != null )
            {
                var val = Enum.Parse( p.PropertyType, fogModeStr, ignoreCase: true );
                p.SetValue( light, val );
            }
        }

        if ( args.TryGetProperty( "fog_strength", out var fsEl ) && fsEl.ValueKind == JsonValueKind.Number )
        {
            var p = light.GetType().GetProperty( "FogStrength" );
            if ( p != null ) p.SetValue( light, fsEl.GetSingle() );
        }

        return HandlerBase.Confirm( $"Configured light on '{go.Name}' ({light.GetType().Name})." );
    }

    // ── create_skybox ──────────────────────────────────────────────────────
    // Ported from LightingToolHandlers.CreateSkyBox

    private static object CreateSkybox( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_skybox" );

        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;
        var skyMaterial = HandlerBase.GetString( args, "material", "materials/skybox/skybox_day_01.vmat" );

        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Sky Box";
        go.WorldPosition = position;

        var sky = go.Components.Create<SkyBox2D>();

        if ( !string.IsNullOrEmpty( skyMaterial ) )
        {
            var mat = Material.Load( skyMaterial );
            if ( mat != null ) sky.SkyMaterial = mat;
        }

        var tintStr = HandlerBase.GetString( args, "tint" );
        if ( !string.IsNullOrEmpty( tintStr ) )
        {
            try { sky.Tint = Color.Parse( tintStr ) ?? default; } catch { }
        }

        if ( args.TryGetProperty( "indirect_lighting", out var iblEl ) &&
             ( iblEl.ValueKind == JsonValueKind.True || iblEl.ValueKind == JsonValueKind.False ) )
        {
            sky.SkyIndirectLighting = iblEl.GetBoolean();
        }

        return HandlerBase.Success( new
        {
            message = $"Created SkyBox '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    // ── set_skybox ─────────────────────────────────────────────────────────
    // Ported from LightingToolHandlers.SetSkyBox

    private static object SetSkybox( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "set_skybox" );

        SkyBox2D sky = null;
        var id = HandlerBase.GetString( args, "id" );
        if ( !string.IsNullOrEmpty( id ) )
        {
            var go = SceneHelpers.FindByIdOrThrow( scene, id, "set_skybox" );
            sky = go.Components.GetAll().FirstOrDefault( c => c is SkyBox2D ) as SkyBox2D;
            if ( sky == null )
                return HandlerBase.Error( $"No SkyBox2D component found on '{go.Name}'.", "set_skybox" );
        }
        else
        {
            // Auto-find the first SkyBox2D in the scene
            foreach ( var go in SceneHelpers.WalkAll( scene, true ) )
            {
                sky = go.Components.GetAll().FirstOrDefault( c => c is SkyBox2D ) as SkyBox2D;
                if ( sky != null ) break;
            }
            if ( sky == null )
                return HandlerBase.Error( "No SkyBox2D found in scene. Provide 'id' or add a SkyBox2D component.", "set_skybox" );
        }

        var matStr = HandlerBase.GetString( args, "material" );
        if ( !string.IsNullOrEmpty( matStr ) )
        {
            var mat = Material.Load( matStr );
            if ( mat != null ) sky.SkyMaterial = mat;
        }

        var tintStr = HandlerBase.GetString( args, "tint" );
        if ( !string.IsNullOrEmpty( tintStr ) )
        {
            try { sky.Tint = Color.Parse( tintStr ) ?? default; } catch { }
        }

        if ( args.TryGetProperty( "indirect_lighting", out var iblEl ) &&
             ( iblEl.ValueKind == JsonValueKind.True || iblEl.ValueKind == JsonValueKind.False ) )
        {
            sky.SkyIndirectLighting = iblEl.GetBoolean();
        }

        return HandlerBase.Confirm( $"Updated SkyBox on '{sky.GameObject.Name}'." );
    }
}
