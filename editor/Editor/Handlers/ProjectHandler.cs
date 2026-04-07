// Editor/Handlers/ProjectHandler.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// project tool: get_collision, set_collision_rule, get_input, get_info.
/// Ported from ProjectSettingsHandlers.cs (199 lines).
/// </summary>
internal static class ProjectHandler
{
    private static readonly JsonSerializerOptions WriteJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "get_collision"      => GetCollision(),
                "set_collision_rule" => SetCollisionRule( args ),
                "get_input"          => GetInput(),
                "get_info"           => GetInfo(),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: get_collision, set_collision_rule, get_input, get_info" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── get_collision ─────────────────────────────────────────────────
    // Ported from ProjectSettingsHandlers.GetCollisionConfig

    private static object GetCollision()
    {
        if ( !Editor.FileSystem.ProjectSettings.FileExists( "Collision.config" ) )
            return HandlerBase.Error( "Collision.config not found in ProjectSettings/.", "get_collision" );

        var content = Editor.FileSystem.ProjectSettings.ReadAllText( "Collision.config" );
        return HandlerBase.Text( content );
    }

    // ── set_collision_rule ────────────────────────────────────────────
    // Ported from ProjectSettingsHandlers.SetCollisionRule

    private static object SetCollisionRule( JsonElement args )
    {
        var layerA = HandlerBase.GetString( args, "layer_a" );
        var layerB = HandlerBase.GetString( args, "layer_b" );

        if ( string.IsNullOrEmpty( layerA ) || string.IsNullOrEmpty( layerB ) )
            return HandlerBase.Error( "Missing required 'layer_a' and 'layer_b' parameters.", "set_collision_rule" );

        if ( args.ValueKind == JsonValueKind.Undefined || !args.TryGetProperty( "collides", out _ ) )
            return HandlerBase.Error( "Missing required 'collides' parameter.", "set_collision_rule" );

        var collides = HandlerBase.GetBool( args, "collides" ) ? "Collide" : "Ignore";

        if ( !Editor.FileSystem.ProjectSettings.FileExists( "Collision.config" ) )
            return HandlerBase.Error( "Collision.config not found.", "set_collision_rule" );

        var content = Editor.FileSystem.ProjectSettings.ReadAllText( "Collision.config" );
        using var doc = JsonDocument.Parse( content );
        var root = doc.RootElement;

        // Parse existing pairs
        var pairs = new List<Dictionary<string, string>>();
        if ( root.TryGetProperty( "Pairs", out var pairsEl ) )
        {
            foreach ( var pair in pairsEl.EnumerateArray() )
            {
                pairs.Add( new Dictionary<string, string>
                {
                    ["a"] = pair.GetProperty( "a" ).GetString(),
                    ["b"] = pair.GetProperty( "b" ).GetString(),
                    ["r"] = pair.GetProperty( "r" ).GetString()
                } );
            }
        }

        // Find existing pair (check both orderings)
        var existing = pairs.FindIndex( p =>
            ( p["a"] == layerA && p["b"] == layerB ) ||
            ( p["a"] == layerB && p["b"] == layerA ) );

        if ( existing >= 0 )
            pairs[existing]["r"] = collides;
        else
            pairs.Add( new Dictionary<string, string> { ["a"] = layerA, ["b"] = layerB, ["r"] = collides } );

        // Build output preserving Version and Defaults
        var output = new Dictionary<string, object>();
        if ( root.TryGetProperty( "Version", out var ver ) )
            output["Version"] = ver.GetInt32();
        if ( root.TryGetProperty( "Defaults", out var def ) )
            output["Defaults"] = JsonSerializer.Deserialize<Dictionary<string, string>>( def.GetRawText() );
        output["Pairs"] = pairs;

        Editor.FileSystem.ProjectSettings.WriteAllText( "Collision.config",
            JsonSerializer.Serialize( output, WriteJson ) );

        return HandlerBase.Confirm( $"Set collision rule: {layerA} <-> {layerB} = {collides}" );
    }

    // ── get_input ─────────────────────────────────────────────────────
    // Ported from ProjectSettingsHandlers.GetInputConfig

    private static object GetInput()
    {
        if ( !Editor.FileSystem.ProjectSettings.FileExists( "Input.config" ) )
            return HandlerBase.Error( "Input.config not found in ProjectSettings/.", "get_input" );

        var content = Editor.FileSystem.ProjectSettings.ReadAllText( "Input.config" );
        return HandlerBase.Text( content );
    }

    // ── get_info ──────────────────────────────────────────────────────
    // Ported from ProjectSettingsHandlers.GetProjectInfo

    private static object GetInfo()
    {
        var libPath = Editor.FileSystem.Libraries.GetFullPath( "" );
        var projectDir = Path.GetDirectoryName( libPath );
        if ( projectDir == null )
            return HandlerBase.Error( "Could not locate project root.", "get_info" );

        var sbprojFiles = Directory.GetFiles( projectDir, "*.sbproj" );
        if ( sbprojFiles.Length == 0 )
            return HandlerBase.Error( "No .sbproj found in project root.", "get_info" );

        var content = File.ReadAllText( sbprojFiles[0] );
        return HandlerBase.Text( content );
    }
}
