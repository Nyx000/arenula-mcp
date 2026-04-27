using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// component tool: add, remove, set_property, set_enabled, get_properties, get_types, copy.
/// </summary>
internal static class ComponentHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "add"            => Add( args ),
                "remove"         => Remove( args ),
                "set_property"   => SetProperty( args ),
                "set_enabled"    => SetEnabled( args ),
                "get_properties" => GetProperties( args ),
                "get_types"      => GetTypes( args ),
                "copy"           => Copy( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}' for tool 'component'.", action,
                    "Valid actions: add, remove, set_property, set_enabled, get_properties, get_types, copy" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── Shared helpers ────────────────────────────────────────────────

    /// <summary>
    /// Find a component on a GO by its GUID string.
    /// Returns null if not found.
    /// </summary>
    private static Component FindComponentById( GameObject go, string componentId )
    {
        if ( string.IsNullOrEmpty( componentId ) ) return null;
        if ( !Guid.TryParse( componentId, out var guid ) ) return null;
        return go.Components.GetAll().FirstOrDefault( c => c.Id == guid );
    }

    /// <summary>
    /// Find a component on a GO by GUID, or throw with a helpful message.
    /// </summary>
    private static Component FindComponentByIdOrThrow( GameObject go, string componentId, string action )
    {
        var comp = FindComponentById( go, componentId );
        if ( comp == null )
        {
            var available = go.Components.GetAll()
                .Select( c => $"{c.GetType().Name} ({c.Id})" )
                .ToList();
            var hint = available.Count > 0
                ? $"Available components: {string.Join( ", ", available )}"
                : "This object has no components.";
            throw new ArgumentException( $"Component '{componentId}' not found on '{go.Name}'. {hint}" );
        }
        return comp;
    }

    /// <summary>
    /// Fast component type lookup using TypeLibrary (indexed).
    /// Prefers game-assembly types over Sandbox built-ins when names collide.
    /// </summary>
    private static TypeDescription FindComponentTypeDescription( string typeName )
    {
        TypeDescription fallback = null;
        foreach ( var candidate in TypeLibrary.GetTypes<Component>() )
        {
            if ( !candidate.Name.Equals( typeName, StringComparison.OrdinalIgnoreCase ) )
                continue;

            // Prefer game types (not in Sandbox namespace) over engine built-ins
            var ns = candidate.TargetType.Namespace ?? "";
            if ( !ns.StartsWith( "Sandbox", StringComparison.OrdinalIgnoreCase ) )
                return candidate;

            fallback ??= candidate;
        }

        if ( fallback != null ) return fallback;

        // Last resort: try exact match by full type name
        var td = TypeLibrary.GetType( typeName );
        if ( td != null && td.TargetType.IsClass && !td.TargetType.IsAbstract
            && typeof( Component ).IsAssignableFrom( td.TargetType ) )
            return td;

        return null;
    }

    // ── add ──────────────────────────────────────────────────────────

    private static object Add( JsonElement args )
    {
        var scene = HandlerBase.RequireScene( "add" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "add" );

        var type = HandlerBase.GetString( args, "type" );
        if ( string.IsNullOrEmpty( type ) )
            return HandlerBase.Error( "Missing required 'type' parameter.", "add" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "add" );

        var td = FindComponentTypeDescription( type );
        if ( td == null )
        {
            // Suggest similar type names
            var suggestions = TypeLibrary.GetTypes<Component>()
                .Where( t => !t.IsAbstract && t.Name.IndexOf( type, StringComparison.OrdinalIgnoreCase ) >= 0 )
                .Select( t => t.Name )
                .Take( 10 )
                .ToList();

            string hint = suggestions.Count > 0
                ? $"Similar types: {string.Join( ", ", suggestions )}"
                : "No similar types found. Use component.get_types to search.";

            return HandlerBase.Error( $"Component type '{type}' not found. {hint}", "add" );
        }

        var comp = go.Components.Create( td );

        // Post-condition: confirm component actually landed on the GameObject.
        var verifiedComp = go.Components.GetAll().FirstOrDefault( c => c.Id == comp.Id );
        if ( verifiedComp == null )
            return HandlerBase.Error(
                $"Component '{td.Name}' reported added but not found on '{go.Name}' after verification. " +
                "Engine-level silent drop.",
                "add",
                "Check 'editor.get_log' for related engine warnings." );

        return HandlerBase.Success( new
        {
            id = go.Id.ToString(),
            name = go.Name,
            component_id = comp.Id.ToString(),
            component_type = comp.GetType().Name,
            message = $"Added '{td.Name}' to '{go.Name}'.",
            verified = new
            {
                component_exists = true,
                component_type = verifiedComp.GetType().Name
            }
        } );
    }

    // ── remove ───────────────────────────────────────────────────────

    private static object Remove( JsonElement args )
    {
        var scene = HandlerBase.RequireScene( "remove" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "remove" );

        var componentId = HandlerBase.GetString( args, "component_id" );
        if ( string.IsNullOrEmpty( componentId ) )
            return HandlerBase.Error( "Missing required 'component_id' parameter.", "remove" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "remove" );
        var comp = FindComponentByIdOrThrow( go, componentId, "remove" );

        var typeName = comp.GetType().Name;
        var compGuid = comp.Id;
        comp.Destroy();

        // Post-condition: component should NOT be on GameObject after remove.
        var stillPresent = go.Components.GetAll().Any( c => c.Id == compGuid );
        if ( stillPresent )
            return HandlerBase.Error(
                $"Component reported removed but still present on '{go.Name}' after verification.",
                "remove",
                "Engine-level silent retention." );

        return HandlerBase.Success( new
        {
            id = go.Id.ToString(),
            name = go.Name,
            message = $"Removed '{typeName}' from '{go.Name}'.",
            verified = new { component_removed = true }
        } );
    }

    // ── set_property ─────────────────────────────────────────────────

    private static object SetProperty( JsonElement args )
    {
        var scene = HandlerBase.RequireScene( "set_property" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "set_property" );

        var componentId = HandlerBase.GetString( args, "component_id" );
        if ( string.IsNullOrEmpty( componentId ) )
            return HandlerBase.Error( "Missing required 'component_id' parameter.", "set_property" );

        var propName = HandlerBase.GetString( args, "property" );
        if ( string.IsNullOrEmpty( propName ) )
            return HandlerBase.Error( "Missing required 'property' parameter.", "set_property" );

        if ( !args.TryGetProperty( "value", out var valEl ) )
            return HandlerBase.Error( "Missing required 'value' parameter.", "set_property" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "set_property" );
        var comp = FindComponentByIdOrThrow( go, componentId, "set_property" );

        var prop = comp.GetType().GetProperty( propName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance );
        if ( prop == null )
            return HandlerBase.Error( $"Property '{propName}' not found on '{comp.GetType().Name}'.", "set_property" );
        if ( !prop.CanWrite )
            return HandlerBase.Error( $"Property '{propName}' is read-only.", "set_property" );

        object converted = ConvertJsonValue( valEl, prop.PropertyType );
        prop.SetValue( comp, converted );
        var readback = prop.GetValue( comp );

        // Post-condition: for resource-valued properties, read back and confirm non-null.
        // Check by PROPERTY TYPE (not read-back value) so we can detect null-after-set.
        object verifiedPayload;

        var propType = prop.PropertyType;
        bool isResourceType = typeof( GameResource ).IsAssignableFrom( propType )
                              || propType == typeof( Model )
                              || propType == typeof( Material );

        if ( isResourceType )
        {
            // Distinguish "user wanted null" from "lookup failed and returned null".
            // Caller may legitimately clear a resource property (e.g.
            // MaterialOverride → null restores the model's default material)
            // by passing JSON null, an empty string, or the literal "null".
            // Without this guard, the post-condition fires on every successful clear.
            bool intentionalNull =
                valEl.ValueKind == JsonValueKind.Null ||
                ( valEl.ValueKind == JsonValueKind.String &&
                  ( string.IsNullOrEmpty( valEl.GetString() ) ||
                    valEl.GetString() == "null" ) );

            if ( readback == null && !intentionalNull )
                return HandlerBase.Error(
                    $"Property '{propName}' on {comp.GetType().Name} was set to '{valEl}' but read back as null. " +
                    "Resource lookup likely failed — check that the asset is under 'Assets/' and indexed.",
                    "set_property",
                    "Use 'asset_query.get_status' to verify the asset is indexed." );

            verifiedPayload = readback switch
            {
                null            => new { cleared = true } as object,
                GameResource gr => new { asset_name = gr.ResourceName, asset_path = gr.ResourcePath } as object,
                Model mdl       => new { model_path = mdl.ResourcePath } as object,
                Material mat    => new { material_path = mat.ResourcePath } as object,
                _               => readback.ToString()
            };
        }
        else
        {
            // Scalar or non-resource — include formatted value for transparency.
            verifiedPayload = FormatReadback( readback );
        }

        return HandlerBase.Success( new
        {
            id = go.Id.ToString(),
            name = go.Name,
            component_type = comp.GetType().Name,
            property = propName,
            value = FormatReadback( readback ),
            message = $"Set '{comp.GetType().Name}.{propName}' = {FormatReadback( readback )}",
            verified = verifiedPayload
        } );
    }

    // Render readback of complex wrapper structs in a way that surfaces the
    // actual values, not the type name. Default ToString on these structs
    // returns "Sandbox.ParticleFloat" which makes set_property responses
    // useless for verification — caller can't tell whether the value stuck.
    private static string FormatReadback( object value )
    {
        return value switch
        {
            null => null,
            ParticleFloat pf => FormatParticleFloat( pf ),
            ParticleVector3 pv => $"({FormatParticleFloat( pv.X )}, {FormatParticleFloat( pv.Y )}, {FormatParticleFloat( pv.Z )})",
            _ => value.ToString()
        };
    }

    private static string FormatParticleFloat( ParticleFloat pf )
    {
        return pf.Type switch
        {
            ParticleFloat.ValueType.Constant   => $"Constant({pf.ConstantValue})",
            ParticleFloat.ValueType.Range      => $"Range({pf.ConstantA},{pf.ConstantB})",
            ParticleFloat.ValueType.Curve      => "Curve(<not-rendered>)",
            ParticleFloat.ValueType.CurveRange => "CurveRange(<not-rendered>)",
            _ => "Unknown"
        };
    }

    private static BBox ParseBBox( JsonElement el )
    {
        // Accept three string forms:
        //   "x1,y1,z1;x2,y2,z2"                     — semicolon separates mins from maxs
        //   "mins x,y,z, maxs x,y,z"                — get_properties readback format
        //   "x1,y1,z1,x2,y2,z2"                     — six comma-separated floats
        if ( el.ValueKind == JsonValueKind.String )
        {
            var s = el.GetString().Trim();

            // Form 2: parse "mins x,y,z, maxs x,y,z"
            if ( s.Contains( "mins" ) && s.Contains( "maxs" ) )
            {
                var minsIdx = s.IndexOf( "mins", StringComparison.OrdinalIgnoreCase );
                var maxsIdx = s.IndexOf( "maxs", StringComparison.OrdinalIgnoreCase );
                var minsPart = s.Substring( minsIdx + 4, maxsIdx - minsIdx - 4 ).Trim().TrimEnd( ',' ).Trim();
                var maxsPart = s.Substring( maxsIdx + 4 ).Trim();
                return new BBox(
                    HandlerBase.ParseVector3( minsPart ),
                    HandlerBase.ParseVector3( maxsPart ) );
            }

            // Form 1: "x1,y1,z1;x2,y2,z2"
            if ( s.Contains( ';' ) )
            {
                var halves = s.Split( ';' );
                if ( halves.Length != 2 )
                    throw new ArgumentException(
                        $"BBox semicolon form must be 'x1,y1,z1;x2,y2,z2', got '{s}'." );
                return new BBox(
                    HandlerBase.ParseVector3( halves[0] ),
                    HandlerBase.ParseVector3( halves[1] ) );
            }

            // Form 3: six comma-separated floats
            var parts = s.Split( ',' );
            if ( parts.Length != 6 )
                throw new ArgumentException(
                    $"BBox must be 'x1,y1,z1;x2,y2,z2' or six comma-separated floats, got '{s}'." );
            return new BBox(
                new Vector3(
                    float.Parse( parts[0].Trim(), System.Globalization.CultureInfo.InvariantCulture ),
                    float.Parse( parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture ),
                    float.Parse( parts[2].Trim(), System.Globalization.CultureInfo.InvariantCulture ) ),
                new Vector3(
                    float.Parse( parts[3].Trim(), System.Globalization.CultureInfo.InvariantCulture ),
                    float.Parse( parts[4].Trim(), System.Globalization.CultureInfo.InvariantCulture ),
                    float.Parse( parts[5].Trim(), System.Globalization.CultureInfo.InvariantCulture ) ) );
        }

        // JSON object: {"Mins": {x,y,z}, "Maxs": {x,y,z}} or with lowercase keys
        if ( el.ValueKind == JsonValueKind.Object )
        {
            Vector3 mins = default, maxs = default;
            JsonElement mEl;
            if ( el.TryGetProperty( "Mins", out mEl ) || el.TryGetProperty( "mins", out mEl ) )
                mins = (Vector3)ConvertJsonValue( mEl, typeof( Vector3 ) );
            if ( el.TryGetProperty( "Maxs", out mEl ) || el.TryGetProperty( "maxs", out mEl ) )
                maxs = (Vector3)ConvertJsonValue( mEl, typeof( Vector3 ) );
            return new BBox( mins, maxs );
        }

        throw new ArgumentException(
            $"Cannot convert {el.ValueKind} to BBox. Use a string or {{Mins,Maxs}} object." );
    }

    /// <summary>
    /// Convert a JSON value to the target CLR type.
    /// Supports: string, bool, int, float, double, Vector3, Enum, Model, Component, GameObject.
    /// </summary>
    private static object ConvertJsonValue( JsonElement el, Type targetType )
    {
        if ( targetType == typeof( string ) )
            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();

        if ( targetType == typeof( bool ) )
        {
            if ( el.ValueKind == JsonValueKind.True ) return true;
            if ( el.ValueKind == JsonValueKind.False ) return false;
            if ( el.ValueKind == JsonValueKind.String ) return bool.Parse( el.GetString() );
            return el.GetBoolean();
        }

        if ( targetType == typeof( int ) )
        {
            if ( el.ValueKind == JsonValueKind.String ) return int.Parse( el.GetString() );
            return el.GetInt32();
        }

        if ( targetType == typeof( float ) )
        {
            if ( el.ValueKind == JsonValueKind.String )
                return float.Parse( el.GetString(), System.Globalization.CultureInfo.InvariantCulture );
            return el.GetSingle();
        }

        if ( targetType == typeof( double ) )
        {
            if ( el.ValueKind == JsonValueKind.String )
                return double.Parse( el.GetString(), System.Globalization.CultureInfo.InvariantCulture );
            return el.GetDouble();
        }

        if ( targetType == typeof( Vector3 ) )
        {
            // Accept "x,y,z" string or {x,y,z} object
            if ( el.ValueKind == JsonValueKind.String )
                return HandlerBase.ParseVector3( el.GetString() );
            if ( el.ValueKind == JsonValueKind.Object )
            {
                float vx = 0, vy = 0, vz = 0;
                if ( el.TryGetProperty( "x", out var xp ) ) vx = xp.GetSingle();
                if ( el.TryGetProperty( "y", out var yp ) ) vy = yp.GetSingle();
                if ( el.TryGetProperty( "z", out var zp ) ) vz = zp.GetSingle();
                return new Vector3( vx, vy, vz );
            }
        }

        if ( targetType == typeof( Vector2 ) )
        {
            if ( el.ValueKind == JsonValueKind.String )
                return HandlerBase.ParseVector2( el.GetString() );
            if ( el.ValueKind == JsonValueKind.Object )
            {
                float u = 0, v = 0;
                if ( el.TryGetProperty( "x", out var xp ) ) u = xp.GetSingle();
                else if ( el.TryGetProperty( "u", out var up ) ) u = up.GetSingle();
                if ( el.TryGetProperty( "y", out var yp ) ) v = yp.GetSingle();
                else if ( el.TryGetProperty( "v", out var vp ) ) v = vp.GetSingle();
                return new Vector2( u, v );
            }
        }

        if ( targetType == typeof( Rotation ) )
        {
            // Accept "pitch,yaw,roll" string or {pitch,yaw,roll} object.
            // Rotation has implicit conversion from Angles, which is constructed
            // from pitch/yaw/roll in degrees.
            if ( el.ValueKind == JsonValueKind.String )
            {
                var parts = el.GetString().Split( ',' );
                if ( parts.Length != 3 )
                    throw new ArgumentException(
                        $"Rotation must be 'pitch,yaw,roll', got '{el.GetString()}'." );
                return Rotation.From(
                    pitch: float.Parse( parts[0].Trim(),
                        System.Globalization.CultureInfo.InvariantCulture ),
                    yaw:   float.Parse( parts[1].Trim(),
                        System.Globalization.CultureInfo.InvariantCulture ),
                    roll:  float.Parse( parts[2].Trim(),
                        System.Globalization.CultureInfo.InvariantCulture ) );
            }
            if ( el.ValueKind == JsonValueKind.Object )
            {
                float pitch = 0, yaw = 0, roll = 0;
                if ( el.TryGetProperty( "pitch", out var pp ) ) pitch = pp.GetSingle();
                if ( el.TryGetProperty( "yaw",   out var yp ) ) yaw   = yp.GetSingle();
                if ( el.TryGetProperty( "roll",  out var rp ) ) roll  = rp.GetSingle();
                return Rotation.From( pitch, yaw, roll );
            }
        }

        if ( targetType == typeof( BBox ) )
            return ParseBBox( el );

        if ( targetType == typeof( Color ) )
        {
            var str = el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
            return Color.Parse( str ) ?? default;
        }

        // ParticleFloat — wrapper that varies a float per-particle. Modes:
        //   "5"            → Constant mode, value 5
        //   "0.75,2.6"     → Range mode, picks per-particle in [0.75, 2.6]
        //   5  (JSON num)  → Constant mode, value 5
        //   {Type,Constants,...} (JSON obj) → routed through ParticleFloat.JsonRead
        //                                     for full Curve / CurveRange coverage
        if ( targetType == typeof( ParticleFloat ) )
            return ParseParticleFloat( el );

        // ParticleVector3 — three ParticleFloats (X/Y/Z fields). Format:
        //   "x,y,z"        → Constant mode on each axis
        //   {X:..,Y:..,Z:..} (JSON obj) → each axis routed through ParticleFloat parse
        if ( targetType == typeof( ParticleVector3 ) )
            return ParseParticleVector3( el );

        // ParticleGradient — wrapper for Color-over-life. Constant mode only here:
        //   "r,g,b,a"      → Constant mode, single color
        //   {Type,ConstantValue,...} (JSON obj) → routed through ParticleGradient.JsonRead
        if ( targetType == typeof( ParticleGradient ) )
            return ParseParticleGradient( el );

        if ( targetType.IsEnum )
        {
            var str = el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
            return Enum.Parse( targetType, str, ignoreCase: true );
        }

        // Handle Sandbox.Model
        if ( targetType == typeof( Model ) )
        {
            var path = el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
            if ( string.IsNullOrEmpty( path ) || path == "null" ) return null;
            return Model.Load( path );
        }

        // Handle Sandbox.Material — separate from GameResource (Material's base
        // is Sandbox.Resource, not Sandbox.GameResource). Has its own static Load
        // with internal cache.
        if ( targetType == typeof( Material ) )
        {
            var path = el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
            if ( string.IsNullOrEmpty( path ) || path == "null" ) return null;
            return Material.Load( path );
        }

        // Generic GameResource — covers Sprite, PrefabFile, ClutterCollection,
        // TerrainStorage, SoundEvent, and every project-defined GameResource
        // subclass via ResourceLibrary.Get<T>(path). Reflection-routed so we
        // don't need a per-type branch for every resource the engine adds.
        // The post-condition check downstream confirms the load actually
        // succeeded (returns null + helpful error if asset isn't indexed).
        if ( typeof( GameResource ).IsAssignableFrom( targetType ) )
        {
            var path = el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
            if ( string.IsNullOrEmpty( path ) || path == "null" ) return null;

            // ResourceLibrary.Get<T>(string) — generic method, dispatch via reflection.
            var getMethod = typeof( ResourceLibrary )
                .GetMethods( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static )
                .FirstOrDefault( m =>
                    m.Name == "Get" &&
                    m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof( string ) );

            if ( getMethod == null )
                throw new InvalidOperationException(
                    "ResourceLibrary.Get<T>(string) not found via reflection — engine API may have changed." );

            var generic = getMethod.MakeGenericMethod( targetType );
            return generic.Invoke( null, new object[] { path } );
        }

        // Handle Component references by GUID
        if ( typeof( Component ).IsAssignableFrom( targetType ) )
        {
            var guidStr = el.ValueKind == JsonValueKind.String ? el.GetString() : null;
            if ( el.ValueKind == JsonValueKind.Object )
            {
                if ( el.TryGetProperty( "id", out var idProp ) ) guidStr = idProp.GetString();
            }

            if ( !string.IsNullOrEmpty( guidStr ) && Guid.TryParse( guidStr, out var compGuid ) )
            {
                var scene = SceneHelpers.ResolveScene();
                if ( scene != null )
                {
                    foreach ( var go in SceneHelpers.WalkAll( scene, true ) )
                    {
                        var match = go.Components.GetAll().FirstOrDefault( c => c.Id == compGuid );
                        if ( match != null && targetType.IsAssignableFrom( match.GetType() ) )
                            return match;
                    }
                }
            }
            return null;
        }

        // Handle GameObject references by GUID
        if ( typeof( GameObject ).IsAssignableFrom( targetType ) )
        {
            var guidStr = el.ValueKind == JsonValueKind.String ? el.GetString() : null;
            if ( el.ValueKind == JsonValueKind.Object )
            {
                if ( el.TryGetProperty( "id", out var idProp ) ) guidStr = idProp.GetString();
            }

            if ( !string.IsNullOrEmpty( guidStr ) )
            {
                var scene = SceneHelpers.ResolveScene();
                if ( scene != null )
                    return SceneHelpers.FindById( scene, guidStr );
            }
            return null;
        }

        // Fallback: try ChangeType from string
        var raw = el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
        return Convert.ChangeType( raw, targetType );
    }

    // ── Particle-wrapper parsers ─────────────────────────────────────
    //
    // Why these exist: ParticleFloat / ParticleVector3 / ParticleGradient
    // are struct wrappers used on every particle field (Lifetime, Rate, Scale,
    // Velocity, Alpha, Tint-over-life, ...). They store a constant, a range,
    // a curve, or a curve-range. Convert.ChangeType has no path from string to
    // these types, so set_property previously errored with "Invalid cast from
    // System.String to Sandbox.ParticleFloat" on the most-tuned fields in the
    // particle system. We handle Constant + Range from short string syntax
    // (the common 95% of tuning), and route JSON objects through each type's
    // own JsonRead static method so every mode (Curve / CurveRange / etc.) is
    // supported without re-implementing the curve serialization here.

    private static ParticleFloat ParseParticleFloat( JsonElement el )
    {
        // Number JSON value: 5 → Constant.
        if ( el.ValueKind == JsonValueKind.Number )
        {
            var pf = new ParticleFloat();
            pf.Type = ParticleFloat.ValueType.Constant;
            pf.ConstantValue = el.GetSingle();
            return pf;
        }

        if ( el.ValueKind == JsonValueKind.String )
        {
            var str = el.GetString().Trim();
            var parts = str.Split( ',' );

            if ( parts.Length == 1 )
            {
                var v = float.Parse( parts[0].Trim(),
                    System.Globalization.CultureInfo.InvariantCulture );
                var pf = new ParticleFloat();
                pf.Type = ParticleFloat.ValueType.Constant;
                pf.ConstantValue = v;
                return pf;
            }

            if ( parts.Length == 2 )
            {
                var a = float.Parse( parts[0].Trim(),
                    System.Globalization.CultureInfo.InvariantCulture );
                var b = float.Parse( parts[1].Trim(),
                    System.Globalization.CultureInfo.InvariantCulture );
                // Two-arg ctor sets Range mode. Setting Type explicitly anyway
                // to be robust against any future ctor refactor.
                var pf = new ParticleFloat( a, b );
                pf.Type = ParticleFloat.ValueType.Range;
                return pf;
            }

            throw new ArgumentException(
                $"ParticleFloat string must be 'value' or 'min,max', got '{str}'." );
        }

        if ( el.ValueKind == JsonValueKind.Object )
        {
            // Route through the engine's own deserializer for full mode coverage
            // (Curve, CurveRange) — those reference Sandbox.Curve/CurveRange
            // types we don't want to re-serialize by hand.
            return DeserializeParticleFloatJson( el.GetRawText() );
        }

        throw new ArgumentException(
            $"Cannot convert {el.ValueKind} to ParticleFloat. " +
            "Use a number, 'value' string, 'min,max' string, or full JSON object." );
    }

    private static ParticleFloat DeserializeParticleFloatJson( string json )
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes( json );
        var reader = new System.Text.Json.Utf8JsonReader( bytes );
        reader.Read();   // advance to the first token (StartObject)
        // Utf8JsonReader is a ref struct → pass by ref. JsonRead(reader, type)
        // is documented without a ref qualifier in arenula-api but the
        // underlying ref struct constraint forces ref-passing at the call site.
        return (ParticleFloat)ParticleFloat.JsonRead( ref reader, typeof( ParticleFloat ) );
    }

    private static ParticleVector3 ParseParticleVector3( JsonElement el )
    {
        // String "x,y,z" → constant per-axis (most common case).
        if ( el.ValueKind == JsonValueKind.String )
        {
            var parts = el.GetString().Split( ',' );
            if ( parts.Length != 3 )
                throw new ArgumentException(
                    $"ParticleVector3 string must be 'x,y,z', got '{el.GetString()}'." );

            var pv = new ParticleVector3();
            pv.X = ConstantParticleFloat( float.Parse( parts[0].Trim(),
                System.Globalization.CultureInfo.InvariantCulture ) );
            pv.Y = ConstantParticleFloat( float.Parse( parts[1].Trim(),
                System.Globalization.CultureInfo.InvariantCulture ) );
            pv.Z = ConstantParticleFloat( float.Parse( parts[2].Trim(),
                System.Globalization.CultureInfo.InvariantCulture ) );
            return pv;
        }

        if ( el.ValueKind == JsonValueKind.Object )
        {
            // Per-axis recursive parse — accepts {X:5, Y:"0,2", Z:{..}} mixed shapes.
            var pv = new ParticleVector3();
            if ( el.TryGetProperty( "X", out var xp ) ) pv.X = ParseParticleFloat( xp );
            if ( el.TryGetProperty( "Y", out var yp ) ) pv.Y = ParseParticleFloat( yp );
            if ( el.TryGetProperty( "Z", out var zp ) ) pv.Z = ParseParticleFloat( zp );
            return pv;
        }

        throw new ArgumentException(
            $"Cannot convert {el.ValueKind} to ParticleVector3. " +
            "Use 'x,y,z' string or {X,Y,Z} object." );
    }

    private static ParticleFloat ConstantParticleFloat( float v )
    {
        var pf = new ParticleFloat();
        pf.Type = ParticleFloat.ValueType.Constant;
        pf.ConstantValue = v;
        return pf;
    }

    private static ParticleGradient ParseParticleGradient( JsonElement el )
    {
        // String "r,g,b,a" or "#hex" → Constant mode.
        if ( el.ValueKind == JsonValueKind.String )
        {
            var color = Color.Parse( el.GetString() ) ?? default;
            var pg = new ParticleGradient();
            pg.Type = ParticleGradient.ValueType.Constant;
            pg.ConstantValue = color;
            return pg;
        }

        // JSON object → defer to engine's deserializer; ParticleGradient's
        // Curve/Range modes reference Gradient resources we don't want to
        // construct by hand. If the engine ever exposes a JsonRead like
        // ParticleFloat.JsonRead, route through it here. For now, only
        // Constant mode is supported via this path — author the gradient in
        // the Inspector for non-constant cases.
        throw new ArgumentException(
            "ParticleGradient JSON-object form not supported via MCP. " +
            "Pass an 'r,g,b,a' string for Constant mode, or set the gradient " +
            "via the Inspector for Range/Gradient modes." );
    }

    // ── set_enabled ──────────────────────────────────────────────────

    private static object SetEnabled( JsonElement args )
    {
        var scene = HandlerBase.RequireScene( "set_enabled" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "set_enabled" );

        var componentId = HandlerBase.GetString( args, "component_id" );
        if ( string.IsNullOrEmpty( componentId ) )
            return HandlerBase.Error( "Missing required 'component_id' parameter.", "set_enabled" );

        if ( !args.TryGetProperty( "enabled", out var enabledEl ) )
            return HandlerBase.Error( "Missing required 'enabled' parameter.", "set_enabled" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "set_enabled" );
        var comp = FindComponentByIdOrThrow( go, componentId, "set_enabled" );

        comp.Enabled = enabledEl.GetBoolean();

        return HandlerBase.Success( new
        {
            id = go.Id.ToString(),
            name = go.Name,
            component_id = comp.Id.ToString(),
            component_type = comp.GetType().Name,
            enabled = comp.Enabled,
            message = $"Set '{comp.GetType().Name}' on '{go.Name}' to {(comp.Enabled ? "enabled" : "disabled")}."
        } );
    }

    // ── get_properties ───────────────────────────────────────────────

    private static object GetProperties( JsonElement args )
    {
        var scene = HandlerBase.RequireScene( "get_properties" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "get_properties" );

        var componentId = HandlerBase.GetString( args, "component_id" );
        if ( string.IsNullOrEmpty( componentId ) )
            return HandlerBase.Error( "Missing required 'component_id' parameter.", "get_properties" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "get_properties" );
        var comp = FindComponentByIdOrThrow( go, componentId, "get_properties" );

        var props = new List<object>();
        var type = comp.GetType();

        foreach ( var prop in type.GetProperties( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance ) )
        {
            if ( !prop.CanRead ) continue;

            object val;
            try
            {
                var raw = prop.GetValue( comp );
                val = raw switch
                {
                    null => null,
                    bool b => (object)b,
                    int i => (object)i,
                    float f => (object)MathF.Round( f, 4 ),
                    double d => (object)Math.Round( d, 4 ),
                    string s => (object)s,
                    Enum e => (object)e.ToString(),
                    Vector3 v => (object)HandlerBase.V3( v ),
                    Rotation r => (object)HandlerBase.Rot( r ),
                    ParticleFloat pf => (object)FormatParticleFloat( pf ),
                    ParticleVector3 pv => (object)$"({FormatParticleFloat( pv.X )}, {FormatParticleFloat( pv.Y )}, {FormatParticleFloat( pv.Z )})",
                    _ => (object)raw.ToString()
                };
            }
            catch
            {
                val = "<error reading value>";
            }

            props.Add( new
            {
                name = prop.Name,
                type = prop.PropertyType.Name,
                value = val,
                canWrite = prop.CanWrite
            } );
        }

        return HandlerBase.Success( new
        {
            gameObjectId = go.Id.ToString(),
            gameObjectName = go.Name,
            componentId = comp.Id.ToString(),
            componentType = comp.GetType().Name,
            enabled = comp.Enabled,
            properties = props
        } );
    }

    // ── get_types ─────────────────────────────────────────────────────

    private static object GetTypes( JsonElement args )
    {
        var filter = HandlerBase.GetString( args, "filter" );

        var results = new List<object>();

        foreach ( var td in TypeLibrary.GetTypes<Component>() )
        {
            if ( td.TargetType != null && td.TargetType.IsAbstract ) continue;

            var name = td.Name;
            if ( !string.IsNullOrEmpty( filter )
                && name.IndexOf( filter, StringComparison.OrdinalIgnoreCase ) < 0
                && (td.TargetType?.Namespace ?? "").IndexOf( filter, StringComparison.OrdinalIgnoreCase ) < 0 )
                continue;

            results.Add( new
            {
                name,
                @namespace = td.TargetType?.Namespace ?? ""
            } );
        }

        results = results.OrderBy( r => ((dynamic)r).name ).ToList();

        return HandlerBase.Success( new
        {
            summary = $"Found {results.Count} component type(s)" +
                (!string.IsNullOrEmpty( filter ) ? $" matching '{filter}'" : "") + ".",
            results
        } );
    }

    // ── copy ──────────────────────────────────────────────────────────

    private static object Copy( JsonElement args )
    {
        var scene = HandlerBase.RequireScene( "copy" );

        var sourceComponentId = HandlerBase.GetString( args, "source_component_id" );
        if ( string.IsNullOrEmpty( sourceComponentId ) )
            return HandlerBase.Error( "Missing required 'source_component_id' parameter.", "copy" );

        var targetId = HandlerBase.GetString( args, "target_id" );
        if ( string.IsNullOrEmpty( targetId ) )
            return HandlerBase.Error( "Missing required 'target_id' parameter.", "copy" );

        // Find source component by scanning all objects
        Component sourceComp = null;
        if ( Guid.TryParse( sourceComponentId, out var sourceGuid ) )
        {
            foreach ( var go in SceneHelpers.WalkAll( scene, true ) )
            {
                sourceComp = go.Components.GetAll().FirstOrDefault( c => c.Id == sourceGuid );
                if ( sourceComp != null ) break;
            }
        }

        if ( sourceComp == null )
            return HandlerBase.Error( $"Source component '{sourceComponentId}' not found in scene.", "copy" );

        var targetGo = SceneHelpers.FindByIdOrThrow( scene, targetId, "copy" );

        // Find the TypeDescription for the source component's type
        var td = FindComponentTypeDescription( sourceComp.GetType().Name );
        if ( td == null )
            return HandlerBase.Error( $"Component type '{sourceComp.GetType().Name}' not found in TypeLibrary.", "copy" );

        var newComp = targetGo.Components.Create( td );

        return HandlerBase.Success( new
        {
            source_component_id = sourceComp.Id.ToString(),
            source_component_type = sourceComp.GetType().Name,
            source_object = sourceComp.GameObject.Name,
            target_id = targetGo.Id.ToString(),
            target_name = targetGo.Name,
            new_component_id = newComp.Id.ToString(),
            message = $"Copied '{sourceComp.GetType().Name}' from '{sourceComp.GameObject.Name}' to '{targetGo.Name}'."
        } );
    }
}
