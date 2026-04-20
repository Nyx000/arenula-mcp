using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// physics tool: add_collider, configure_collider, add_rigidbody,
/// create_model_physics, create_character_controller, create_joint.
/// Consolidates Ozmium AddCollider + AddPlaneCollider + AddHullCollider
/// into a single 'add_collider' action with type param.
/// CreateJoint moved here from EffectToolHandlers.
/// </summary>
internal static class PhysicsHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "add_collider"                => AddCollider( args ),
                "configure_collider"          => ConfigureCollider( args ),
                "add_rigidbody"               => AddRigidbody( args ),
                "create_model_physics"        => CreateModelPhysics( args ),
                "create_character_controller" => CreateCharacterController( args ),
                "create_joint"                => CreateJoint( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: add_collider, configure_collider, add_rigidbody, create_model_physics, create_character_controller, create_joint" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── add_collider ──────────────────────────────────────────────────
    // Consolidates: AddCollider, AddPlaneCollider, AddHullCollider

    private static object AddCollider( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "add_collider" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "add_collider" );

        var type = HandlerBase.GetString( args, "type", "box" );
        var go = SceneHelpers.FindByIdOrThrow( scene, id, "add_collider" );

        switch ( type.ToLowerInvariant() )
        {
            case "box":
            case "boxcollider":
            {
                var c = go.Components.Create<BoxCollider>();
                var sizeStr = HandlerBase.GetString( args, "size" );
                if ( sizeStr != null ) c.Scale = HandlerBase.ParseVector3( sizeStr );
                var centerStr = HandlerBase.GetString( args, "center" );
                if ( centerStr != null ) c.Center = HandlerBase.ParseVector3( centerStr );
                ApplyCommonColliderProps( c, args );
                return HandlerBase.Success( new
                {
                    message = $"Added BoxCollider to '{go.Name}'.",
                    component_id = c.Id.ToString()
                } );
            }
            case "sphere":
            case "spherecollider":
            {
                var c = go.Components.Create<SphereCollider>();
                var centerStr = HandlerBase.GetString( args, "center" );
                if ( centerStr != null ) c.Center = HandlerBase.ParseVector3( centerStr );
                if ( HandlerBase.TryGetFloat( args, "radius", out var rad ) ) c.Radius = rad;
                ApplyCommonColliderProps( c, args );
                return HandlerBase.Success( new
                {
                    message = $"Added SphereCollider to '{go.Name}'.",
                    component_id = c.Id.ToString()
                } );
            }
            case "capsule":
            case "capsulecollider":
            {
                var c = go.Components.Create<CapsuleCollider>();
                var startStr = HandlerBase.GetString( args, "start" );
                if ( startStr != null ) c.Start = HandlerBase.ParseVector3( startStr );
                var endStr = HandlerBase.GetString( args, "end" );
                if ( endStr != null ) c.End = HandlerBase.ParseVector3( endStr );
                if ( HandlerBase.TryGetFloat( args, "radius", out var rad ) ) c.Radius = rad;
                ApplyCommonColliderProps( c, args );
                return HandlerBase.Success( new
                {
                    message = $"Added CapsuleCollider to '{go.Name}'.",
                    component_id = c.Id.ToString()
                } );
            }
            case "model":
            case "modelcollider":
            {
                var c = go.Components.Create<ModelCollider>();
                ApplyCommonColliderProps( c, args );
                return HandlerBase.Success( new
                {
                    message = $"Added ModelCollider to '{go.Name}'.",
                    component_id = c.Id.ToString()
                } );
            }
            case "plane":
            case "planecollider":
            {
                var c = go.Components.Create<PlaneCollider>();
                var sizeStr = HandlerBase.GetString( args, "size" );
                if ( sizeStr != null )
                {
                    var sv = HandlerBase.ParseVector3( sizeStr );
                    c.Scale = new Vector2( sv.x, sv.y );
                }
                var centerStr = HandlerBase.GetString( args, "center" );
                if ( centerStr != null ) c.Center = HandlerBase.ParseVector3( centerStr );
                // PlaneCollider has no IsTrigger/Friction/Elasticity, so no common props
                return HandlerBase.Success( new
                {
                    message = $"Added PlaneCollider to '{go.Name}'.",
                    component_id = c.Id.ToString()
                } );
            }
            case "hull":
            case "hullcollider":
            {
                var c = go.Components.Create<HullCollider>();
                var hullType = HandlerBase.GetString( args, "hull_type", "Box" );
                c.Type = HandlerBase.RequireEnum<HullCollider.PrimitiveType>( hullType, "hull_type", "create_hull_collider" );
                var centerStr = HandlerBase.GetString( args, "center" );
                if ( centerStr != null ) c.Center = HandlerBase.ParseVector3( centerStr );
                if ( c.Type == HullCollider.PrimitiveType.Box )
                {
                    var sizeStr = HandlerBase.GetString( args, "size" );
                    if ( sizeStr != null ) c.BoxSize = HandlerBase.ParseVector3( sizeStr );
                }
                else
                {
                    if ( HandlerBase.TryGetFloat( args, "height", out var h ) ) c.Height = h;
                    if ( HandlerBase.TryGetFloat( args, "radius", out var r ) ) c.Radius = r;
                    if ( c.Type == HullCollider.PrimitiveType.Cone )
                    {
                        if ( HandlerBase.TryGetFloat( args, "tip_radius", out var tr ) ) c.Radius2 = tr;
                    }
                    if ( HandlerBase.TryGetInt( args, "slices", out var sl ) ) c.Slices = sl;
                }
                ApplyCommonColliderProps( c, args );
                return HandlerBase.Success( new
                {
                    message = $"Added HullCollider ({c.Type}) to '{go.Name}'.",
                    component_id = c.Id.ToString()
                } );
            }
            default:
                return HandlerBase.Error(
                    $"Unknown collider type '{type}'.",
                    "add_collider",
                    "Valid types: box, sphere, capsule, model, plane, hull" );
        }
    }

    private static void ApplyCommonColliderProps( Collider collider, JsonElement args )
    {
        if ( HandlerBase.TryGetBool( args, "is_trigger", out var trig ) ) collider.IsTrigger = trig;

        var surfaceStr = HandlerBase.GetString( args, "surface" );
        if ( !string.IsNullOrEmpty( surfaceStr ) )
        {
            // Surface is set via the Surface property (resource path)
            var surfProp = collider.GetType().GetProperty( "Surface" );
            if ( surfProp != null )
            {
                var surface = ResourceLibrary.Get<Surface>( surfaceStr );
                if ( surface == null )
                    throw new InvalidOperationException(
                        $"Surface not found at '{surfaceStr}'. Surface resources must be indexed — user-created .surface assets must live under 'Assets/'." );
                surfProp.SetValue( collider, surface );
            }
        }

        if ( HandlerBase.TryGetFloat( args, "friction", out var fr ) ) collider.Friction = fr;
        if ( HandlerBase.TryGetFloat( args, "elasticity", out var el ) ) collider.Elasticity = el;
    }

    // ── configure_collider ────────────────────────────────────────────

    private static object ConfigureCollider( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "configure_collider" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "configure_collider" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "configure_collider" );

        // Find collider — prefer specific component_id if given
        Collider collider;
        var compId = HandlerBase.GetString( args, "component_id" );
        if ( !string.IsNullOrEmpty( compId ) && Guid.TryParse( compId, out var cGuid ) )
        {
            collider = go.Components.GetAll().FirstOrDefault( c => c is Collider && c.Id == cGuid ) as Collider;
            if ( collider == null )
                return HandlerBase.Error( $"Component '{compId}' is not a Collider on '{go.Name}'.", "configure_collider" );
        }
        else
        {
            collider = go.Components.GetAll().FirstOrDefault( c => c is Collider ) as Collider;
            if ( collider == null )
                return HandlerBase.Error( $"No Collider component found on '{go.Name}'.", "configure_collider" );
        }

        // BoxCollider-specific
        if ( collider is BoxCollider bc )
        {
            var sizeStr = HandlerBase.GetString( args, "size" );
            if ( sizeStr != null ) bc.Scale = HandlerBase.ParseVector3( sizeStr );
            var centerStr = HandlerBase.GetString( args, "center" );
            if ( centerStr != null ) bc.Center = HandlerBase.ParseVector3( centerStr );
        }

        // SphereCollider-specific
        if ( collider is SphereCollider sc )
        {
            var centerStr = HandlerBase.GetString( args, "center" );
            if ( centerStr != null ) sc.Center = HandlerBase.ParseVector3( centerStr );
            if ( HandlerBase.TryGetFloat( args, "radius", out var r ) ) sc.Radius = r;
        }

        // CapsuleCollider-specific
        if ( collider is CapsuleCollider cc )
        {
            var startStr = HandlerBase.GetString( args, "start" );
            if ( startStr != null ) cc.Start = HandlerBase.ParseVector3( startStr );
            var endStr = HandlerBase.GetString( args, "end" );
            if ( endStr != null ) cc.End = HandlerBase.ParseVector3( endStr );
            if ( HandlerBase.TryGetFloat( args, "radius", out var cr ) ) cc.Radius = cr;
        }

        // Common properties
        ApplyCommonColliderProps( collider, args );

        return HandlerBase.Confirm( $"Configured {collider.GetType().Name} on '{go.Name}'." );
    }

    // ── add_rigidbody ─────────────────────────────────────────────────

    private static object AddRigidbody( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "add_rigidbody" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "add_rigidbody" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "add_rigidbody" );

        var rb = go.Components.Create<Rigidbody>();
        if ( HandlerBase.TryGetFloat( args, "mass", out var mass ) ) rb.MassOverride = mass;
        if ( HandlerBase.TryGetFloat( args, "linear_damping", out var ld ) ) rb.LinearDamping = ld;
        if ( HandlerBase.TryGetFloat( args, "angular_damping", out var ad ) ) rb.AngularDamping = ad;
        rb.Gravity = HandlerBase.GetBool( args, "gravity", true );
        if ( HandlerBase.TryGetFloat( args, "gravity_scale", out var gs ) ) rb.GravityScale = gs;
        if ( HandlerBase.TryGetBool( args, "enhanced_ccd", out var ccd ) ) rb.EnhancedCcd = ccd;

        return HandlerBase.Success( new
        {
            message = $"Added Rigidbody to '{go.Name}'.",
            component_id = rb.Id.ToString(),
            mass_override = rb.MassOverride,
            gravity = rb.Gravity,
            enhanced_ccd = rb.EnhancedCcd
        } );
    }

    // ── create_model_physics ──────────────────────────────────────────

    private static object CreateModelPhysics( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_model_physics" );

        // This action can work on an existing GO (via id) or create a new one
        var id = HandlerBase.GetString( args, "id" );
        GameObject go;

        if ( !string.IsNullOrEmpty( id ) )
        {
            go = SceneHelpers.FindByIdOrThrow( scene, id, "create_model_physics" );
        }
        else
        {
            go = scene.CreateObject();
            go.Name = HandlerBase.GetString( args, "name" ) ?? "Model Physics";
            var posStr = HandlerBase.GetString( args, "position" );
            if ( posStr != null ) go.WorldPosition = HandlerBase.ParseVector3( posStr );
        }

        var mp = go.Components.Create<ModelPhysics>();

        var modelPath = HandlerBase.GetString( args, "model_path" );
        if ( !string.IsNullOrEmpty( modelPath ) )
            mp.Model = HandlerBase.RequireModel( modelPath, "create_map_physics" );

        mp.MotionEnabled = HandlerBase.GetBool( args, "motion_enabled", true );

        return HandlerBase.Success( new
        {
            message = $"Created ModelPhysics on '{go.Name}'.",
            id = go.Id.ToString(),
            component_id = mp.Id.ToString(),
            model = mp.Model?.ResourcePath ?? "null"
        } );
    }

    // ── create_character_controller ───────────────────────────────────

    private static object CreateCharacterController( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_character_controller" );

        // This action can work on existing GO (via id) or create a new one
        var id = HandlerBase.GetString( args, "id" );
        GameObject go;

        if ( !string.IsNullOrEmpty( id ) )
        {
            go = SceneHelpers.FindByIdOrThrow( scene, id, "create_character_controller" );
        }
        else
        {
            go = scene.CreateObject();
            go.Name = HandlerBase.GetString( args, "name" ) ?? "Character Controller";
            var posStr = HandlerBase.GetString( args, "position" );
            if ( posStr != null ) go.WorldPosition = HandlerBase.ParseVector3( posStr );
        }

        var cc = go.Components.Create<CharacterController>();
        if ( HandlerBase.TryGetFloat( args, "radius", out var r ) ) cc.Radius = r;
        if ( HandlerBase.TryGetFloat( args, "height", out var h ) ) cc.Height = h;
        if ( HandlerBase.TryGetFloat( args, "step_height", out var sh ) ) cc.StepHeight = sh;
        if ( HandlerBase.TryGetFloat( args, "ground_angle", out var ga ) ) cc.GroundAngle = ga;
        if ( HandlerBase.TryGetFloat( args, "acceleration", out var ac ) ) cc.Acceleration = ac;
        if ( HandlerBase.TryGetFloat( args, "bounciness", out var bn ) ) cc.Bounciness = bn;

        return HandlerBase.Success( new
        {
            message = $"Created CharacterController on '{go.Name}'.",
            id = go.Id.ToString(),
            component_id = cc.Id.ToString(),
            radius = cc.Radius,
            height = cc.Height
        } );
    }

    // ── create_joint ──────────────────────────────────────────────────
    // MOVED from EffectToolHandlers.CreateJoint to physics (better fit)

    private static object CreateJoint( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_joint" );

        var type = HandlerBase.GetString( args, "type", "fixed" );
        var bodyA = HandlerBase.GetString( args, "body_a" );
        if ( string.IsNullOrEmpty( bodyA ) )
            return HandlerBase.Error( "Missing required 'body_a' parameter (GUID of first body).", "create_joint" );

        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Joint";
        var posStr = HandlerBase.GetString( args, "position" );
        if ( posStr != null ) go.WorldPosition = HandlerBase.ParseVector3( posStr );

        Joint joint = type.ToLowerInvariant() switch
        {
            "ball"    => go.Components.Create<BallJoint>(),
            "hinge"   => go.Components.Create<HingeJoint>(),
            "slider"  => go.Components.Create<SliderJoint>(),
            "spring"  => go.Components.Create<SpringJoint>(),
            "wheel"   => go.Components.Create<WheelJoint>(),
            "upright" => go.Components.Create<UprightJoint>(),
            _         => go.Components.Create<FixedJoint>()
        };

        if ( HandlerBase.TryGetFloat( args, "break_force", out var bf ) ) joint.BreakForce = bf;
        if ( HandlerBase.TryGetFloat( args, "break_torque", out var bt ) ) joint.BreakTorque = bt;

        // UprightJoint-specific properties
        if ( joint is UprightJoint uj )
        {
            if ( HandlerBase.TryGetFloat( args, "hertz", out var hz ) ) uj.Hertz = hz;
            if ( HandlerBase.TryGetFloat( args, "damping_ratio", out var dr ) ) uj.DampingRatio = dr;
            if ( HandlerBase.TryGetFloat( args, "max_torque", out var mt ) ) uj.MaxTorque = mt;
        }

        // Link body_a
        var bodyAGo = SceneHelpers.FindByIdOrThrow( scene, bodyA, "create_joint" );
        joint.Body = bodyAGo;

        // Link body_b (optional anchor body)
        var bodyB = HandlerBase.GetString( args, "body_b" );
        joint.AnchorBody = HandlerBase.ResolveGameObjectById( scene, bodyB, "create_joint" );

        return HandlerBase.Success( new
        {
            message = $"Created {type} joint '{go.Name}'.",
            id = go.Id.ToString(),
            component_id = joint.Id.ToString(),
            type,
            body_a = bodyA,
            body_b = bodyB
        } );
    }
}
