// Editor/Handlers/EffectsHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// effects tool: create, configure_particle, configure_post_processing.
/// Consolidates Ozmium CreateParticleEffect, CreateFogVolume, CreateBeamEffect,
/// CreateVerletRope, CreateRadiusDamage, CreateRenderEntity into 'create' with type param.
/// </summary>
internal static class EffectsHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "create"                    => Create( args ),
                "configure_particle"        => ConfigureParticle( args ),
                "configure_post_processing" => ConfigurePostProcessing( args ),
                "configure_sprite"          => ConfigureSprite( args ),
                "configure_prop"            => ConfigureProp( args ),
                "configure_world_panel"     => ConfigureWorldPanel( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: create, configure_particle, configure_post_processing, configure_sprite, configure_prop, configure_world_panel" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── create ────────────────────────────────────────────────────────
    // Consolidates: CreateParticleEffect, CreateFogVolume, CreateBeamEffect,
    //               CreateVerletRope, CreateRadiusDamage, CreateRenderEntity

    private static object Create( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create" );

        var type = HandlerBase.GetString( args, "type", "particle" );
        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;

        return type.ToLowerInvariant() switch
        {
            "particle"       => CreateParticle( scene, args, position ),
            "fog"            => CreateFog( scene, args, position ),
            "beam"           => CreateBeam( scene, args, position ),
            "rope"           => CreateRope( scene, args, position ),
            "radius_damage"  => CreateRadiusDamage( scene, args, position ),
            "render_entity"  => CreateRenderEntity( scene, args, position ),
            "sprite"         => CreateSprite( scene, args, position ),
            "prop"           => CreateProp( scene, args, position ),
            "world_panel"    => CreateWorldPanel( scene, args, position ),
            _ => HandlerBase.Error(
                $"Unknown effect type '{type}'.",
                "create",
                "Valid types: particle, fog, beam, rope, radius_damage, render_entity, sprite, prop, world_panel" )
        };
    }

    private static object CreateParticle( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Particle Effect";
        go.WorldPosition = position;

        var pe = go.Components.Create<ParticleEffect>();
        pe.MaxParticles = HandlerBase.GetInt( args, "max_particles", 1000 );
        if ( HandlerBase.TryGetFloat( args, "lifetime", out var lt ) ) pe.Lifetime = lt;
        if ( HandlerBase.TryGetFloat( args, "time_scale", out var ts ) ) pe.TimeScale = ts;
        if ( HandlerBase.TryGetFloat( args, "pre_warm", out var pw ) ) pe.PreWarm = pw;

        return HandlerBase.Success( new
        {
            message = $"Created ParticleEffect '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    private static object CreateFog( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Fog Volume";
        go.WorldPosition = position;

        var fogType = HandlerBase.GetString( args, "fog_type", "gradient" );

        if ( fogType.Equals( "volumetric", StringComparison.OrdinalIgnoreCase ) )
        {
            var vf = go.Components.Create<VolumetricFogVolume>();
            if ( HandlerBase.TryGetFloat( args, "strength", out var s ) ) vf.Strength = s;
            if ( HandlerBase.TryGetFloat( args, "falloff_exponent", out var fe ) ) vf.FalloffExponent = fe;
            vf.Bounds = BBox.FromPositionAndSize( 0, 300 );
        }
        else
        {
            var gf = go.Components.Create<GradientFog>();
            gf.Color = Color.White;
            if ( HandlerBase.TryGetFloat( args, "height", out var h ) ) gf.Height = h;
            if ( HandlerBase.TryGetFloat( args, "start_distance", out var sd ) ) gf.StartDistance = sd;
            if ( HandlerBase.TryGetFloat( args, "end_distance", out var ed ) ) gf.EndDistance = ed;
            if ( HandlerBase.TryGetFloat( args, "falloff_exponent", out var fe ) ) gf.FalloffExponent = fe;
            if ( HandlerBase.TryGetFloat( args, "vertical_falloff_exponent", out var vfe ) ) gf.VerticalFalloffExponent = vfe;
            HandlerBase.ApplyColor( args, "color", c => gf.Color = c );
        }

        return HandlerBase.Success( new
        {
            message = $"Created {fogType} fog on '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    private static object CreateBeam( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Beam Effect";
        go.WorldPosition = position;

        var beam = go.Components.Create<BeamEffect>();
        if ( HandlerBase.TryGetFloat( args, "scale", out var sc ) ) beam.Scale = sc;
        if ( HandlerBase.TryGetFloat( args, "beams_per_second", out var bps ) ) beam.BeamsPerSecond = bps;
        beam.MaxBeams = HandlerBase.GetInt( args, "max_beams", 1 );
        beam.Looped = HandlerBase.GetBool( args, "looped", false );

        var targetPosStr = HandlerBase.GetString( args, "target_position" );
        if ( targetPosStr != null )
            beam.TargetPosition = HandlerBase.ParseVector3( targetPosStr );

        var targetId = HandlerBase.GetString( args, "target_id" );
        beam.TargetGameObject = HandlerBase.ResolveGameObjectById( scene, targetId, "create_beam" );

        return HandlerBase.Success( new
        {
            message = $"Created BeamEffect '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    private static object CreateRope( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Verlet Rope";
        go.WorldPosition = position;

        var rope = go.Components.Create<VerletRope>();
        rope.SegmentCount = HandlerBase.GetInt( args, "segment_count", 16 );
        if ( HandlerBase.TryGetFloat( args, "slack", out var sl ) ) rope.Slack = sl;
        if ( HandlerBase.TryGetFloat( args, "radius", out var r ) ) rope.Radius = r;
        if ( HandlerBase.TryGetFloat( args, "stiffness", out var st ) ) rope.Stiffness = st;
        if ( HandlerBase.TryGetFloat( args, "damping_factor", out var df ) ) rope.DampingFactor = df;

        var attachId = HandlerBase.GetString( args, "attachment_id" );
        rope.Attachment = HandlerBase.ResolveGameObjectById( scene, attachId, "create_rope" );

        return HandlerBase.Success( new
        {
            message = $"Created VerletRope '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    private static object CreateRadiusDamage( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Radius Damage";
        go.WorldPosition = position;

        var rd = go.Components.Create<RadiusDamage>();
        if ( HandlerBase.TryGetFloat( args, "radius", out var r ) ) rd.Radius = r;
        if ( HandlerBase.TryGetFloat( args, "damage_amount", out var da ) ) rd.DamageAmount = da;
        if ( HandlerBase.TryGetFloat( args, "physics_force_scale", out var pfs ) ) rd.PhysicsForceScale = pfs;
        rd.DamageOnEnabled = HandlerBase.GetBool( args, "damage_on_enabled", true );
        rd.Occlusion = HandlerBase.GetBool( args, "occlusion", true );

        var damageTags = HandlerBase.GetString( args, "damage_tags" );
        if ( !string.IsNullOrEmpty( damageTags ) )
        {
            foreach ( var tag in damageTags.Split( ',', StringSplitOptions.RemoveEmptyEntries ) )
                rd.DamageTags.Add( tag.Trim() );
        }

        return HandlerBase.Success( new
        {
            message = $"Created RadiusDamage '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition ),
            radius = rd.Radius,
            damage = rd.DamageAmount
        } );
    }

    private static object CreateRenderEntity( Scene scene, JsonElement args, Vector3 position )
    {
        // Delegates to RenderingToolHandlers-equivalent logic
        // Supports: text, line, sprite, trail, model_renderer, skinned_model, screen_panel
        var renderType = HandlerBase.GetString( args, "render_type", "" );

        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Render Entity";
        go.WorldPosition = position;

        switch ( renderType.ToLowerInvariant() )
        {
            case "text":
            {
                var tr = go.Components.Create<TextRenderer>();
                tr.Text = HandlerBase.GetString( args, "text", tr.Text );
                HandlerBase.ApplyColor( args, "color", c => tr.Color = c );
                break;
            }
            case "trail":
            {
                var trail = go.Components.Create<TrailRenderer>();
                trail.MaxPoints = HandlerBase.GetInt( args, "max_points", trail.MaxPoints );
                trail.Emitting = HandlerBase.GetBool( args, "emitting", trail.Emitting );
                break;
            }
            case "line":
                go.Components.Create<LineRenderer>();
                break;
            case "model_renderer":
            {
                var mr = go.Components.Create<ModelRenderer>();
                var modelPath = HandlerBase.GetString( args, "model_path" );
                var resolvedModel = HandlerBase.ResolveModel( modelPath, "create_effect" );
                if ( resolvedModel != null ) mr.Model = resolvedModel;
                break;
            }
            case "skinned_model":
            {
                var sk = go.Components.Create<SkinnedModelRenderer>();
                var modelPath = HandlerBase.GetString( args, "model_path" );
                var resolvedModel = HandlerBase.ResolveModel( modelPath, "create_effect" );
                if ( resolvedModel != null ) sk.Model = resolvedModel;
                break;
            }
            case "screen_panel":
            {
                var sp = go.Components.Create<ScreenPanel>();
                if ( HandlerBase.TryGetFloat( args, "opacity", out var op ) ) sp.Opacity = op;
                break;
            }
            default:
                go.Destroy();
                return HandlerBase.Error(
                    $"Unknown render_type '{renderType}'.",
                    "create",
                    "For render_entity type, valid render_types: text, line, trail, model_renderer, skinned_model, screen_panel" );
        }

        return HandlerBase.Success( new
        {
            message = $"Created {renderType} render entity '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    // ── configure_particle ────────────────────────────────────────────

    private static object ConfigureParticle( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure_particle" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_particle" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "configure_particle" );
        var pe = go.Components.Get<ParticleEffect>();
        if ( pe == null )
            return HandlerBase.Error( $"No ParticleEffect component found on '{go.Name}'.", "configure_particle" );

        if ( HandlerBase.TryGetInt( args, "max_particles", out var mp ) ) pe.MaxParticles = mp;
        if ( HandlerBase.TryGetFloat( args, "lifetime", out var lt ) ) pe.Lifetime = lt;
        if ( HandlerBase.TryGetFloat( args, "time_scale", out var ts ) ) pe.TimeScale = ts;
        if ( HandlerBase.TryGetFloat( args, "pre_warm", out var pw ) ) pe.PreWarm = pw;

        return HandlerBase.Confirm( $"Configured ParticleEffect on '{go.Name}'." );
    }

    // ── configure_post_processing ─────────────────────────────────────

    private static object ConfigurePostProcessing( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure_post_processing" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_post_processing" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "configure_post_processing" );

        // ConfigurePostProcessing in Ozmium creates a new PostProcessVolume if not present
        var pp = go.Components.Get<PostProcessVolume>();
        if ( pp == null )
            pp = go.Components.Create<PostProcessVolume>();

        pp.Priority = HandlerBase.GetInt( args, "priority", pp.Priority );
        if ( HandlerBase.TryGetFloat( args, "blend_weight", out var bw ) ) pp.BlendWeight = bw;
        if ( HandlerBase.TryGetFloat( args, "blend_distance", out var bd ) ) pp.BlendDistance = bd;
        pp.EditorPreview = HandlerBase.GetBool( args, "editor_preview", pp.EditorPreview );

        return HandlerBase.Confirm( $"Configured PostProcessVolume on '{go.Name}'." );
    }

    // ── CreateSprite ─────────────────────────────────────────────────

    private static object CreateSprite( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Sprite";
        go.WorldPosition = position;

        var sr = go.Components.Create<SpriteRenderer>();

        var spritePath = HandlerBase.GetString( args, "sprite" );
        if ( !string.IsNullOrEmpty( spritePath ) )
        {
            var sprite = ResourceLibrary.Get<Sprite>( spritePath );
            if ( sprite == null )
                return HandlerBase.Error(
                    $"Sprite not found at '{spritePath}'.",
                    "create",
                    "Sprite assets must be indexed. User-created sprites belong under 'Assets/'; paths are relative to Assets/." );
            sr.Sprite = sprite;
        }

        var sizeStr = HandlerBase.GetString( args, "size" );
        if ( sizeStr != null ) sr.Size = HandlerBase.ParseVector2( sizeStr );

        HandlerBase.ApplyColor( args, "color", c => sr.Color = c );

        var billboard = HandlerBase.GetString( args, "billboard" );
        var bmParsed = HandlerBase.ResolveEnum<SpriteRenderer.BillboardMode>( billboard, "billboard", "create_sprite" );
        if ( bmParsed.HasValue ) sr.Billboard = bmParsed.Value;

        if ( HandlerBase.TryGetBool( args, "lighting", out var lit ) ) sr.Lighting = lit;
        if ( HandlerBase.TryGetBool( args, "shadows", out var sh ) ) sr.Shadows = sh;
        if ( HandlerBase.TryGetBool( args, "opaque", out var op ) ) sr.Opaque = op;
        if ( HandlerBase.TryGetBool( args, "flip_horizontal", out var fh ) ) sr.FlipHorizontal = fh;
        if ( HandlerBase.TryGetBool( args, "flip_vertical", out var fv ) ) sr.FlipVertical = fv;

        var animation = HandlerBase.GetString( args, "animation" );
        if ( !string.IsNullOrEmpty( animation ) )
            sr.StartingAnimationName = animation;
        if ( HandlerBase.TryGetFloat( args, "playback_speed", out var ps ) ) sr.PlaybackSpeed = ps;

        return HandlerBase.Success( new
        {
            message = $"Created SpriteRenderer '{go.Name}'.",
            id = go.Id.ToString(),
            component_id = sr.Id.ToString(),
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    // ── CreateProp ───────────────────────────────────────────────────

    private static object CreateProp( Scene scene, JsonElement args, Vector3 position )
    {
        var modelPath = HandlerBase.GetString( args, "model" );
        if ( string.IsNullOrEmpty( modelPath ) )
            return HandlerBase.Error( "Missing required 'model' parameter for prop type.", "create" );

        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Prop";
        go.WorldPosition = position;

        var prop = go.Components.Create<Prop>();
        var resolvedModel = HandlerBase.ResolveModel( modelPath, "create_prop" );
        if ( resolvedModel != null ) prop.Model = resolvedModel;

        HandlerBase.ApplyColor( args, "tint", c => prop.Tint = c );

        if ( HandlerBase.TryGetFloat( args, "health", out var h ) ) prop.Health = h;
        if ( HandlerBase.TryGetBool( args, "is_static", out var is ) ) prop.IsStatic = is;
        // Note: Prop.IsFlammable is read-only (derived from model data)
        if ( HandlerBase.TryGetBool( args, "start_asleep", out var sa ) ) prop.StartAsleep = sa;

        var materialGroup = HandlerBase.GetString( args, "material_group" );
        if ( !string.IsNullOrEmpty( materialGroup ) )
            prop.MaterialGroup = materialGroup;

        return HandlerBase.Success( new
        {
            message = $"Created Prop '{go.Name}'.",
            id = go.Id.ToString(),
            component_id = prop.Id.ToString(),
            model = modelPath,
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    // ── CreateWorldPanel ─────────────────────────────────────────────

    private static object CreateWorldPanel( Scene scene, JsonElement args, Vector3 position )
    {
        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "World Panel";
        go.WorldPosition = position;

        var wp = go.Components.Create<WorldPanel>();

        var panelSizeStr = HandlerBase.GetString( args, "panel_size" );
        if ( panelSizeStr != null )
            wp.PanelSize = HandlerBase.ParseVector2( panelSizeStr );

        if ( HandlerBase.TryGetBool( args, "look_at_camera", out var lac ) ) wp.LookAtCamera = lac;
        if ( HandlerBase.TryGetFloat( args, "render_scale", out var rs ) ) wp.RenderScale = rs;
        if ( HandlerBase.TryGetFloat( args, "interaction_range", out var ir ) ) wp.InteractionRange = ir;

        var hAlign = HandlerBase.GetString( args, "horizontal_align" );
        var haParsed = HandlerBase.ResolveEnum<WorldPanel.HAlignment>( hAlign, "horizontal_align", "create_world_panel" );
        if ( haParsed.HasValue ) wp.HorizontalAlign = haParsed.Value;

        var vAlign = HandlerBase.GetString( args, "vertical_align" );
        var vaParsed = HandlerBase.ResolveEnum<WorldPanel.VAlignment>( vAlign, "vertical_align", "create_world_panel" );
        if ( vaParsed.HasValue ) wp.VerticalAlign = vaParsed.Value;

        return HandlerBase.Success( new
        {
            message = $"Created WorldPanel '{go.Name}'.",
            id = go.Id.ToString(),
            component_id = wp.Id.ToString(),
            panel_size = new { x = wp.PanelSize.x, y = wp.PanelSize.y },
            position = HandlerBase.V3( go.WorldPosition )
        } );
    }

    // ── configure_sprite ─────────────────────────────────────────────

    private static object ConfigureSprite( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure_sprite" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_sprite" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "configure_sprite" );

        var sr = SceneHelpers.GetComponentByIdOrFirst<SpriteRenderer>( go, args );

        if ( sr == null )
            return HandlerBase.Error( $"No SpriteRenderer found on '{go.Name}'.", "configure_sprite" );

        var spritePath = HandlerBase.GetString( args, "sprite" );
        if ( !string.IsNullOrEmpty( spritePath ) )
        {
            var sprite = ResourceLibrary.Get<Sprite>( spritePath );
            if ( sprite == null )
                return HandlerBase.Error(
                    $"Sprite not found at '{spritePath}'.",
                    "configure_sprite",
                    "Sprite assets must be indexed. User-created sprites belong under 'Assets/'; paths are relative to Assets/." );
            sr.Sprite = sprite;
        }

        var sizeStr = HandlerBase.GetString( args, "size" );
        if ( sizeStr != null ) sr.Size = HandlerBase.ParseVector2( sizeStr );

        HandlerBase.ApplyColor( args, "color", c => sr.Color = c );

        var billboard = HandlerBase.GetString( args, "billboard" );
        var bmParsed = HandlerBase.ResolveEnum<SpriteRenderer.BillboardMode>( billboard, "billboard", "configure_sprite" );
        if ( bmParsed.HasValue ) sr.Billboard = bmParsed.Value;

        if ( HandlerBase.TryGetBool( args, "lighting", out var lit ) ) sr.Lighting = lit;
        if ( HandlerBase.TryGetBool( args, "shadows", out var sh ) ) sr.Shadows = sh;
        if ( HandlerBase.TryGetBool( args, "opaque", out var op ) ) sr.Opaque = op;
        if ( HandlerBase.TryGetBool( args, "flip_horizontal", out var fh ) ) sr.FlipHorizontal = fh;
        if ( HandlerBase.TryGetBool( args, "flip_vertical", out var fv ) ) sr.FlipVertical = fv;
        if ( HandlerBase.TryGetFloat( args, "playback_speed", out var ps ) ) sr.PlaybackSpeed = ps;
        var animation = HandlerBase.GetString( args, "animation" );
        if ( !string.IsNullOrEmpty( animation ) )
            sr.StartingAnimationName = animation;

        var texFilter = HandlerBase.GetString( args, "texture_filter" );
        var fmParsed = HandlerBase.ResolveEnum<Sandbox.Rendering.FilterMode>( texFilter, "texture_filter", "configure_sprite" );
        if ( fmParsed.HasValue ) sr.TextureFilter = fmParsed.Value;

        if ( HandlerBase.TryGetFloat( args, "depth_feather", out var df ) ) sr.DepthFeather = df;
        if ( HandlerBase.TryGetFloat( args, "fog_strength", out var fs ) ) sr.FogStrength = fs;
        if ( HandlerBase.TryGetFloat( args, "alpha_cutoff", out var ac ) ) sr.AlphaCutoff = ac;

        return HandlerBase.Confirm( $"Configured SpriteRenderer on '{go.Name}'." );
    }

    // ── configure_prop ───────────────────────────────────────────────

    private static object ConfigureProp( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure_prop" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_prop" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "configure_prop" );

        var prop = SceneHelpers.GetComponentByIdOrFirst<Prop>( go, args );

        if ( prop == null )
            return HandlerBase.Error( $"No Prop component found on '{go.Name}'.", "configure_prop" );

        var modelPath = HandlerBase.GetString( args, "model" );
        if ( !string.IsNullOrEmpty( modelPath ) )
        {
            var resolvedModel = HandlerBase.ResolveModel( modelPath, "configure_prop" );
            if ( resolvedModel != null ) prop.Model = resolvedModel;
        }

        HandlerBase.ApplyColor( args, "tint", c => prop.Tint = c );

        if ( HandlerBase.TryGetFloat( args, "health", out var h ) ) prop.Health = h;
        if ( HandlerBase.TryGetBool( args, "is_static", out var is ) ) prop.IsStatic = is;
        // Note: Prop.IsFlammable is read-only (derived from model data)
        if ( HandlerBase.TryGetBool( args, "start_asleep", out var sa ) ) prop.StartAsleep = sa;

        var materialGroup = HandlerBase.GetString( args, "material_group" );
        if ( !string.IsNullOrEmpty( materialGroup ) )
            prop.MaterialGroup = materialGroup;

        if ( args.TryGetProperty( "body_groups", out var bgEl ) && bgEl.ValueKind == JsonValueKind.Number )
            prop.BodyGroups = bgEl.GetUInt64();

        return HandlerBase.Confirm( $"Configured Prop on '{go.Name}'." );
    }

    // ── configure_world_panel ────────────────────────────────────────

    private static object ConfigureWorldPanel( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure_world_panel" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_world_panel" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "configure_world_panel" );

        var wp = SceneHelpers.GetComponentByIdOrFirst<WorldPanel>( go, args );

        if ( wp == null )
            return HandlerBase.Error( $"No WorldPanel found on '{go.Name}'.", "configure_world_panel" );

        var panelSizeStr = HandlerBase.GetString( args, "panel_size" );
        if ( panelSizeStr != null )
            wp.PanelSize = HandlerBase.ParseVector2( panelSizeStr );

        if ( HandlerBase.TryGetBool( args, "look_at_camera", out var lac ) ) wp.LookAtCamera = lac;
        if ( HandlerBase.TryGetFloat( args, "render_scale", out var rs ) ) wp.RenderScale = rs;
        if ( HandlerBase.TryGetFloat( args, "interaction_range", out var ir ) ) wp.InteractionRange = ir;

        var hAlign = HandlerBase.GetString( args, "horizontal_align" );
        var haParsed = HandlerBase.ResolveEnum<WorldPanel.HAlignment>( hAlign, "horizontal_align", "configure_world_panel" );
        if ( haParsed.HasValue ) wp.HorizontalAlign = haParsed.Value;

        var vAlign = HandlerBase.GetString( args, "vertical_align" );
        var vaParsed = HandlerBase.ResolveEnum<WorldPanel.VAlignment>( vAlign, "vertical_align", "configure_world_panel" );
        if ( vaParsed.HasValue ) wp.VerticalAlign = vaParsed.Value;

        return HandlerBase.Confirm( $"Configured WorldPanel on '{go.Name}'." );
    }
}
