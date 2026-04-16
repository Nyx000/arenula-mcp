using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Sandbox;

namespace Arenula;

/// <summary>
/// Shared response helpers used by all tool handlers.
/// Provides canonical response formatting per Arenula MCP spec.
/// </summary>
internal static class HandlerBase
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    internal const int MaxResponseChars = 16384;

    // ── Success responses ──────────────────────────────────────────────

    /// <summary>Wrap a plain text message as an MCP success response.</summary>
    internal static object Text( string text ) => new
    {
        content = new object[] { new { type = "text", text } }
    };

    /// <summary>Serialize an object to JSON and wrap as MCP success response.</summary>
    internal static object Success( object data )
    {
        var json = JsonSerializer.Serialize( data, Json );
        if ( json.Length > MaxResponseChars )
            json = json[..MaxResponseChars] + "\n... [truncated]";
        return Text( json );
    }

    // ── Image responses ─────────────────────────────────────────────────

    /// <summary>Return a base64-encoded image as an MCP image content block, with optional caption.</summary>
    internal static object Image( byte[] imageData, string mimeType = "image/jpeg", string caption = null )
    {
        var content = new List<object>
        {
            new { type = "image", data = Convert.ToBase64String( imageData ), mimeType }
        };
        if ( caption != null )
            content.Add( new { type = "text", text = caption } );
        return new { content };
    }

    // ── Error responses ────────────────────────────────────────────────

    /// <summary>Return a tool execution error with action context and suggestion.</summary>
    internal static object Error( string message, string action = null, string suggestion = null )
    {
        var error = new Dictionary<string, string> { ["error"] = message };
        if ( action != null ) error["action"] = action;
        if ( suggestion != null ) error["suggestion"] = suggestion;
        return new
        {
            content = new object[] { new { type = "text", text = JsonSerializer.Serialize( error, Json ) } },
            isError = true
        };
    }

    /// <summary>Return a protocol-level error (invalid action, missing param).</summary>
    internal static object ProtocolError( int code, string message ) => new
    {
        code, message
    };

    // ── Identifier tuples ──────────────────────────────────────────────

    /// <summary>Canonical { id, name } tuple for a GameObject.</summary>
    internal static object IdTuple( GameObject go ) => new
    {
        id = go.Id.ToString(),
        name = go.Name
    };

    /// <summary>Canonical { id, name } tuple for a Component.</summary>
    internal static object IdTuple( Component c ) => new
    {
        id = c.Id.ToString(),
        name = c.GetType().Name
    };

    // ── Pagination ─────────────────────────────────────────────────────

    /// <summary>Apply offset/limit pagination to a list and wrap with metadata.</summary>
    internal static object Paginate<T>( IReadOnlyList<T> items, int offset, int limit,
        Func<T, object> transform )
    {
        var total = items.Count;
        var page = items.Skip( offset ).Take( limit ).Select( transform ).ToList();
        return Success( new
        {
            results = page,
            total,
            offset,
            limit,
            has_more = offset + limit < total
        } );
    }

    // ── Param extraction helpers ───────────────────────────────────────

    /// <summary>Extract a string parameter from JSON args, or return default.</summary>
    internal static string GetString( JsonElement args, string name, string defaultValue = null )
    {
        if ( args.ValueKind == JsonValueKind.Undefined ) return defaultValue;
        if ( args.TryGetProperty( name, out var el ) && el.ValueKind == JsonValueKind.String )
            return el.GetString();
        return defaultValue;
    }

    /// <summary>Extract an int parameter from JSON args, or return default.</summary>
    internal static int GetInt( JsonElement args, string name, int defaultValue = 0 )
    {
        if ( args.ValueKind == JsonValueKind.Undefined ) return defaultValue;
        if ( args.TryGetProperty( name, out var el ) && el.ValueKind == JsonValueKind.Number )
            return el.GetInt32();
        return defaultValue;
    }

    /// <summary>Extract a bool parameter from JSON args, or return default.</summary>
    internal static bool GetBool( JsonElement args, string name, bool defaultValue = false )
    {
        if ( args.ValueKind == JsonValueKind.Undefined ) return defaultValue;
        if ( args.TryGetProperty( name, out var el ) )
        {
            if ( el.ValueKind == JsonValueKind.True ) return true;
            if ( el.ValueKind == JsonValueKind.False ) return false;
        }
        return defaultValue;
    }

    /// <summary>Extract a float parameter from JSON args, or return default.</summary>
    internal static float GetFloat( JsonElement args, string name, float defaultValue = 0f )
    {
        if ( args.ValueKind == JsonValueKind.Undefined ) return defaultValue;
        if ( args.TryGetProperty( name, out var el ) && el.ValueKind == JsonValueKind.Number )
            return el.GetSingle();
        return defaultValue;
    }

    // ── Vector/rotation helpers ────────────────────────────────────────

    /// <summary>Format a Vector3 as a compact object.</summary>
    internal static object V3( Vector3 v ) => new { x = MathF.Round( v.x, 2 ), y = MathF.Round( v.y, 2 ), z = MathF.Round( v.z, 2 ) };

    /// <summary>Format a Rotation as Euler angles.</summary>
    internal static object Rot( Rotation r )
    {
        var e = r.Angles();
        return new { pitch = MathF.Round( e.pitch, 2 ), yaw = MathF.Round( e.yaw, 2 ), roll = MathF.Round( e.roll, 2 ) };
    }

    /// <summary>Parse a "x,y,z" string into a Vector3.</summary>
    internal static Vector3 ParseVector3( string s )
    {
        var parts = s.Split( ',' );
        if ( parts.Length != 3 ) throw new ArgumentException( $"Expected 'x,y,z' format, got '{s}'" );
        return new Vector3( float.Parse( parts[0].Trim() ), float.Parse( parts[1].Trim() ), float.Parse( parts[2].Trim() ) );
    }

    internal static Vector2 ParseVector2( string s )
    {
        var parts = s.Split( ',' );
        if ( parts.Length != 2 ) throw new ArgumentException( $"Expected 'u,v' format, got '{s}'" );
        return new Vector2( float.Parse( parts[0].Trim() ), float.Parse( parts[1].Trim() ) );
    }

    /// <summary>Parse a comma-separated string of integers "0,1,2" into an int array.</summary>
    internal static int[] ParseIntArray( string s )
    {
        if ( string.IsNullOrWhiteSpace( s ) )
            throw new ArgumentException( "Expected comma-separated integers, got empty string" );
        var parts = s.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
        var result = new int[parts.Length];
        for ( int i = 0; i < parts.Length; i++ )
            result[i] = int.Parse( parts[i] );
        return result;
    }

    // ── Write confirmation ─────────────────────────────────────────────

    /// <summary>Standard write confirmation with ID tuple and message.</summary>
    internal static object WriteConfirm( GameObject go, string message ) => Success( new
    {
        id = go.Id.ToString(),
        name = go.Name,
        message
    } );

    internal static object Confirm( string message ) => Text( message );

    // ── Warning helpers ───────────────────────────────────────────────
    // Construct a single warning entry for the response envelope's "warnings" array.
    // Use when an operation partially succeeded and the caller should know what was skipped.

    internal static object Warning( string message, string field = null, string suggestion = null )
    {
        var w = new Dictionary<string, string> { ["message"] = message };
        if ( field != null ) w["field"] = field;
        if ( suggestion != null ) w["suggestion"] = suggestion;
        return w;
    }

    // ── Resource resolution helpers ───────────────────────────────────
    // RequireResource: errors if path is null/empty or resource not found.
    // ResolveResource: returns null if path is null/empty; errors if provided-but-not-found.
    // Both go through ResourceLibrary.Get<T> — works for any GameResource-derived type.

    internal static T RequireResource<T>( string path, string action, string hint = null ) where T : Resource
    {
        if ( string.IsNullOrEmpty( path ) )
            throw new InvalidOperationException( $"Missing required resource path for '{action}'." );
        var res = ResourceLibrary.Get<T>( path );
        if ( res == null )
        {
            var extra = hint != null ? $" {hint}" : "";
            throw new InvalidOperationException(
                $"{typeof(T).Name} not found at '{path}'.{extra} " +
                "User-authored assets must live under 'Assets/'. Path is relative to Assets/ " +
                "(e.g. 'rifle.weapon' for Assets/rifle.weapon, no 'Assets/' prefix)." );
        }
        return res;
    }

    internal static T ResolveResource<T>( string path, string action, string hint = null ) where T : Resource
    {
        if ( string.IsNullOrEmpty( path ) ) return null;
        return RequireResource<T>( path, action, hint );
    }

    // Model and Material use their own static factories, not ResourceLibrary — separate helpers.

    internal static Model RequireModel( string path, string action )
    {
        if ( string.IsNullOrEmpty( path ) )
            throw new InvalidOperationException( $"Missing required model path for '{action}'." );
        var m = Model.Load( path );
        if ( m == null )
            throw new InvalidOperationException(
                $"Model not found at '{path}'. Check the .vmdl path is valid and the model is mounted or under Assets/." );
        return m;
    }

    internal static Model ResolveModel( string path, string action )
    {
        if ( string.IsNullOrEmpty( path ) ) return null;
        return RequireModel( path, action );
    }

    internal static Material RequireMaterial( string path, string action )
    {
        if ( string.IsNullOrEmpty( path ) )
            throw new InvalidOperationException( $"Missing required material path for '{action}'." );
        var m = Material.Load( path );
        if ( m == null )
            throw new InvalidOperationException(
                $"Material not found at '{path}'. Check the .vmat path is valid and the material is mounted or under Assets/." );
        return m;
    }

    internal static Material ResolveMaterial( string path, string action )
    {
        if ( string.IsNullOrEmpty( path ) ) return null;
        return RequireMaterial( path, action );
    }

    // ── Enum parsing helpers ──────────────────────────────────────────
    // RequireEnum: errors with valid-values list if value is invalid or empty.
    // ResolveEnum: returns null if value is null/empty; errors if provided-but-invalid.

    internal static TEnum RequireEnum<TEnum>( string value, string paramName, string action )
        where TEnum : struct, Enum
    {
        if ( string.IsNullOrEmpty( value ) )
            throw new InvalidOperationException( $"Missing required '{paramName}' for '{action}'." );
        if ( !Enum.TryParse<TEnum>( value, ignoreCase: true, out var result ) )
        {
            var valid = string.Join( ", ", Enum.GetNames<TEnum>() );
            throw new InvalidOperationException(
                $"Invalid {paramName} '{value}' for '{action}'. Valid values: {valid}." );
        }
        return result;
    }

    internal static TEnum? ResolveEnum<TEnum>( string value, string paramName, string action )
        where TEnum : struct, Enum
    {
        if ( string.IsNullOrEmpty( value ) ) return null;
        return RequireEnum<TEnum>( value, paramName, action );
    }

    // ── Project root resolution ───────────────────────────────────────

    /// <summary>
    /// Resolve the project root directory via the current project API.
    /// Returns null if it cannot be determined.
    /// </summary>
    internal static string GetProjectRoot()
    {
        try
        {
            return Project.Current?.GetRootPath();
        }
        catch { return null; }
    }

    /// <summary>
    /// Resolve a relative path to an absolute path under the project root.
    /// Returns null (with error message) if the project root cannot be determined.
    /// </summary>
    internal static string ResolveProjectPath( string relativePath )
    {
        if ( System.IO.Path.IsPathRooted( relativePath ) )
            return relativePath;

        var root = GetProjectRoot();
        if ( string.IsNullOrEmpty( root ) )
            return null;

        return System.IO.Path.Combine( root, relativePath.Replace( '/', System.IO.Path.DirectorySeparatorChar ) );
    }
}
