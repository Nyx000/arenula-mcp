using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;
using Sandbox.Clutter;
using HalfEdgeMesh;

namespace Arenula;

/// <summary>
/// mesh tool: create_block, create_clutter, set_face_material, set_texture_params,
/// set_vertex_position, set_vertex_color, set_vertex_blend, get_info.
/// Ported from MeshEditHandlers.cs (612 lines) + EffectToolHandlers.CreateClutter.
/// </summary>
internal static class MeshHandler
{
    internal static object Handle( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "create_block"        => CreateBlock( args ),
                "create_clutter"      => CreateClutter( args ),
                "set_face_material"   => SetFaceMaterial( args ),
                "set_texture_params"  => SetTextureParams( args ),
                "set_vertex_position" => SetVertexPosition( args ),
                "set_vertex_color"    => SetVertexColor( args ),
                "set_vertex_blend"    => SetVertexBlend( args ),
                "get_info"            => GetInfo( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: create_block, create_clutter, set_face_material, set_texture_params, set_vertex_position, set_vertex_color, set_vertex_blend, get_info" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── create_block ──────────────────────────────────────────────────
    // Ported from MeshEditHandlers.CreateBlock

    private static object CreateBlock( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_block" );

        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;

        var sizeStr = HandlerBase.GetString( args, "size" );
        Vector3 size;
        if ( sizeStr != null )
            size = HandlerBase.ParseVector3( sizeStr );
        else
            size = new Vector3( 100, 100, 100 );

        var materialPath = HandlerBase.GetString( args, "material", "materials/dev/reflectivity_30.vmat" );
        var name = HandlerBase.GetString( args, "name" ) ?? "Block";

        var gameObject = scene.CreateObject();
        gameObject.Name = name;
        gameObject.WorldPosition = position;

        var material = MaterialHelper.LoadMaterialOrDefault( materialPath );

        var boxMins = new Vector3( -size.x / 2f, -size.y / 2f, -size.z / 2f );
        var boxMaxs = new Vector3( size.x / 2f, size.y / 2f, size.z / 2f );

        // Create vertices for a cube (winding matches s&box BlockPrimitive)
        var vertices = new List<Vector3>
        {
            // Front face (z = maxs.z, normal +Z)
            new( boxMins.x, boxMins.y, boxMaxs.z ),
            new( boxMaxs.x, boxMins.y, boxMaxs.z ),
            new( boxMaxs.x, boxMaxs.y, boxMaxs.z ),
            new( boxMins.x, boxMaxs.y, boxMaxs.z ),
            // Back face (z = mins.z, normal -Z)
            new( boxMins.x, boxMaxs.y, boxMins.z ),
            new( boxMaxs.x, boxMaxs.y, boxMins.z ),
            new( boxMaxs.x, boxMins.y, boxMins.z ),
            new( boxMins.x, boxMins.y, boxMins.z ),
            // Top face (y = maxs.y, normal +Y)
            new( boxMaxs.x, boxMaxs.y, boxMins.z ),
            new( boxMins.x, boxMaxs.y, boxMins.z ),
            new( boxMins.x, boxMaxs.y, boxMaxs.z ),
            new( boxMaxs.x, boxMaxs.y, boxMaxs.z ),
            // Bottom face (y = mins.y, normal -Y)
            new( boxMaxs.x, boxMins.y, boxMaxs.z ),
            new( boxMins.x, boxMins.y, boxMaxs.z ),
            new( boxMins.x, boxMins.y, boxMins.z ),
            new( boxMaxs.x, boxMins.y, boxMins.z ),
            // Right face (x = maxs.x, normal +X)
            new( boxMaxs.x, boxMaxs.y, boxMaxs.z ),
            new( boxMaxs.x, boxMins.y, boxMaxs.z ),
            new( boxMaxs.x, boxMins.y, boxMins.z ),
            new( boxMaxs.x, boxMaxs.y, boxMins.z ),
            // Left face (x = mins.x, normal -X)
            new( boxMins.x, boxMaxs.y, boxMins.z ),
            new( boxMins.x, boxMins.y, boxMins.z ),
            new( boxMins.x, boxMins.y, boxMaxs.z ),
            new( boxMins.x, boxMaxs.y, boxMaxs.z )
        };

        var mesh = new PolygonMesh();
        var hVertices = mesh.AddVertices( vertices.ToArray() );

        var faceDefinitions = new[]
        {
            new[] { 0, 1, 2, 3 },     // Front
            new[] { 4, 5, 6, 7 },     // Back
            new[] { 8, 9, 10, 11 },   // Top
            new[] { 12, 13, 14, 15 }, // Bottom
            new[] { 16, 17, 18, 19 }, // Right
            new[] { 20, 21, 22, 23 }  // Left
        };

        var hFaces = new List<FaceHandle>();
        foreach ( var faceIndices in faceDefinitions )
        {
            var faceVerts = faceIndices.Select( i => hVertices[i] ).ToArray();
            var hFace = mesh.AddFace( faceVerts );
            hFaces.Add( hFace );
        }

        foreach ( var hFace in hFaces )
            mesh.SetFaceMaterial( hFace, material );

        mesh.TextureAlignToGrid( mesh.Transform );
        mesh.SetSmoothingAngle( 40.0f );

        var meshComponent = gameObject.Components.Create<MeshComponent>();
        meshComponent.Mesh = mesh;
        meshComponent.RebuildMesh();

        gameObject.Tags.Add( "mesh" );
        gameObject.Tags.Add( "block" );
        gameObject.Tags.Add( "building" );

        return HandlerBase.Success( new
        {
            message = $"Created block '{name}' ({size.x}x{size.y}x{size.z}).",
            id = gameObject.Id.ToString(),
            name = gameObject.Name,
            position = HandlerBase.V3( gameObject.WorldPosition ),
            face_count = hFaces.Count,
            vertex_count = hVertices.Length,
            material = materialPath
        } );
    }

    // ── create_clutter ────────────────────────────────────────────────
    // MOVED from EffectToolHandlers.CreateClutter (better fit in mesh)

    private static object CreateClutter( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_clutter" );

        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;

        var go = scene.CreateObject();
        go.Name = HandlerBase.GetString( args, "name" ) ?? "Clutter";
        go.WorldPosition = position;

        var clutter = go.Components.Create<ClutterComponent>();
        clutter.Seed = HandlerBase.GetInt( args, "seed", 0 );

        var mode = HandlerBase.GetString( args, "mode", "Volume" );
        if ( Enum.TryParse<ClutterComponent.ClutterMode>( mode, true, out var cm ) )
            clutter.Mode = cm;

        var defPath = HandlerBase.GetString( args, "definition" );
        if ( !string.IsNullOrEmpty( defPath ) )
        {
            var asset = AssetSystem.FindByPath( defPath );
            if ( asset != null )
            {
                var def = asset.LoadResource<ClutterDefinition>();
                if ( def != null ) clutter.Clutter = def;
            }
        }

        return HandlerBase.Success( new
        {
            message = $"Created Clutter '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            position = HandlerBase.V3( go.WorldPosition ),
            mode = clutter.Mode.ToString()
        } );
    }

    // ── set_face_material ─────────────────────────────────────────────
    // Ported from MeshEditHandlers.SetFaceMaterial

    private static object SetFaceMaterial( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "set_face_material" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "set_face_material" );

        var materialPath = HandlerBase.GetString( args, "material" );
        if ( string.IsNullOrEmpty( materialPath ) )
            return HandlerBase.Error( "Missing required 'material' parameter.", "set_face_material" );

        var faceIndex = HandlerBase.GetInt( args, "face_index", -1 );
        var go = SceneHelpers.FindByIdOrThrow( scene, id, "set_face_material" );

        var meshComponent = go.Components.Get<MeshComponent>();
        if ( meshComponent == null )
            return HandlerBase.Error( $"GameObject '{go.Name}' has no MeshComponent.", "set_face_material" );

        var mesh = meshComponent.Mesh;
        var material = MaterialHelper.LoadMaterial( materialPath );
        if ( material == null )
            return HandlerBase.Error( $"Failed to load material '{materialPath}'.", "set_face_material" );

        if ( faceIndex >= 0 )
        {
            if ( !MaterialHelper.ApplyMaterialToFace( mesh, faceIndex, material ) )
                return HandlerBase.Error( $"Invalid face index {faceIndex}.", "set_face_material" );

            return HandlerBase.Confirm( $"Applied material '{materialPath}' to face {faceIndex} on '{go.Name}'." );
        }
        else
        {
            int count = 0;
            foreach ( var hFace in mesh.FaceHandles )
            {
                mesh.SetFaceMaterial( hFace, material );
                count++;
            }
            return HandlerBase.Confirm( $"Applied material '{materialPath}' to {count} faces on '{go.Name}'." );
        }
    }

    // ── set_texture_params ────────────────────────────────────────────
    // Ported from MeshEditHandlers.SetTextureParameters

    private static object SetTextureParams( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "set_texture_params" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "set_texture_params" );

        var faceIndex = HandlerBase.GetInt( args, "face_index", -1 );
        var go = SceneHelpers.FindByIdOrThrow( scene, id, "set_texture_params" );

        var meshComponent = go.Components.Get<MeshComponent>();
        if ( meshComponent == null )
            return HandlerBase.Error( $"GameObject '{go.Name}' has no MeshComponent.", "set_texture_params" );

        var mesh = meshComponent.Mesh;

        // Parse UV axis vectors from "x,y,z" strings or use defaults
        var uAxisStr = HandlerBase.GetString( args, "u_axis" );
        var vAxisStr = HandlerBase.GetString( args, "v_axis" );
        var vAxisU = uAxisStr != null ? HandlerBase.ParseVector3( uAxisStr ) : new Vector3( 1, 0, 0 );
        var vAxisV = vAxisStr != null ? HandlerBase.ParseVector3( vAxisStr ) : new Vector3( 0, 0, 1 );

        var scaleStr = HandlerBase.GetString( args, "scale" );
        Vector2 scale;
        if ( scaleStr != null )
        {
            scale = HandlerBase.ParseVector2( scaleStr );
        }
        else
        {
            scale = new Vector2( 1, 1 );
        }

        if ( faceIndex >= 0 )
        {
            if ( !MaterialHelper.SetTextureParameters( mesh, faceIndex, vAxisU, vAxisV, scale ) )
                return HandlerBase.Error( $"Invalid face index {faceIndex}.", "set_texture_params" );
        }
        else
        {
            foreach ( var hFace in mesh.FaceHandles )
                mesh.SetFaceTextureParameters( hFace, vAxisU, vAxisV, scale );
        }

        return HandlerBase.Confirm( faceIndex >= 0
            ? $"Set texture parameters for face {faceIndex} on '{go.Name}'."
            : $"Set texture parameters for all faces on '{go.Name}'." );
    }

    // ── set_vertex_position ───────────────────────────────────────────
    // Ported from MeshEditHandlers.SetVertexPosition

    private static object SetVertexPosition( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "set_vertex_position" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "set_vertex_position" );

        var vertexIndex = HandlerBase.GetInt( args, "vertex_index", -1 );
        if ( vertexIndex < 0 )
            return HandlerBase.Error( "Missing required 'vertex_index' parameter.", "set_vertex_position" );

        var posStr = HandlerBase.GetString( args, "position" );
        if ( posStr == null )
            return HandlerBase.Error( "Missing required 'position' parameter.", "set_vertex_position" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "set_vertex_position" );
        var meshComponent = go.Components.Get<MeshComponent>();
        if ( meshComponent == null )
            return HandlerBase.Error( $"GameObject '{go.Name}' has no MeshComponent.", "set_vertex_position" );

        var mesh = meshComponent.Mesh;
        var hVertex = mesh.VertexHandleFromIndex( vertexIndex );
        if ( !hVertex.IsValid )
            return HandlerBase.Error( $"Invalid vertex index {vertexIndex}.", "set_vertex_position" );

        var newPosition = HandlerBase.ParseVector3( posStr );
        mesh.SetVertexPosition( hVertex, newPosition );

        return HandlerBase.Confirm( $"Set vertex {vertexIndex} position to {posStr} on '{go.Name}'." );
    }

    // ── set_vertex_color ──────────────────────────────────────────────
    // Ported from MeshEditHandlers.SetVertexColor

    private static object SetVertexColor( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "set_vertex_color" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "set_vertex_color" );

        var vertexIndex = HandlerBase.GetInt( args, "vertex_index", -1 );
        if ( vertexIndex < 0 )
            return HandlerBase.Error( "Missing required 'vertex_index' parameter.", "set_vertex_color" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "set_vertex_color" );
        var meshComponent = go.Components.Get<MeshComponent>();
        if ( meshComponent == null )
            return HandlerBase.Error( $"GameObject '{go.Name}' has no MeshComponent.", "set_vertex_color" );

        var mesh = meshComponent.Mesh;
        var hVertex = mesh.VertexHandleFromIndex( vertexIndex );
        if ( !hVertex.IsValid )
            return HandlerBase.Error( $"Invalid vertex index {vertexIndex}.", "set_vertex_color" );

        var hHalfEdge = mesh.HalfEdgeHandleFromIndex( vertexIndex );
        if ( !hHalfEdge.IsValid )
            return HandlerBase.Error( $"Invalid half-edge for vertex {vertexIndex}.", "set_vertex_color" );

        var colorStr = HandlerBase.GetString( args, "color" );
        Color color;
        if ( !string.IsNullOrEmpty( colorStr ) )
        {
            try { color = Color.Parse( colorStr ) ?? Color.White; } catch { color = Color.White; }
        }
        else
        {
            color = new Color(
                HandlerBase.GetFloat( args, "r", 1f ),
                HandlerBase.GetFloat( args, "g", 1f ),
                HandlerBase.GetFloat( args, "b", 1f ),
                HandlerBase.GetFloat( args, "a", 1f ) );
        }

        mesh.SetVertexColor( hHalfEdge, color );

        return HandlerBase.Confirm( $"Set vertex {vertexIndex} color on '{go.Name}'." );
    }

    // ── set_vertex_blend ──────────────────────────────────────────────
    // Ported from MeshEditHandlers.SetVertexBlend

    private static object SetVertexBlend( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "set_vertex_blend" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "set_vertex_blend" );

        var vertexIndex = HandlerBase.GetInt( args, "vertex_index", -1 );
        if ( vertexIndex < 0 )
            return HandlerBase.Error( "Missing required 'vertex_index' parameter.", "set_vertex_blend" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "set_vertex_blend" );
        var meshComponent = go.Components.Get<MeshComponent>();
        if ( meshComponent == null )
            return HandlerBase.Error( $"GameObject '{go.Name}' has no MeshComponent.", "set_vertex_blend" );

        var mesh = meshComponent.Mesh;
        var hVertex = mesh.VertexHandleFromIndex( vertexIndex );
        if ( !hVertex.IsValid )
            return HandlerBase.Error( $"Invalid vertex index {vertexIndex}.", "set_vertex_blend" );

        var hHalfEdge = mesh.HalfEdgeHandleFromIndex( vertexIndex );
        if ( !hHalfEdge.IsValid )
            return HandlerBase.Error( $"Invalid half-edge for vertex {vertexIndex}.", "set_vertex_blend" );

        var blend = new Color(
            HandlerBase.GetFloat( args, "r", 0f ),
            HandlerBase.GetFloat( args, "g", 0f ),
            HandlerBase.GetFloat( args, "b", 0f ),
            HandlerBase.GetFloat( args, "blend", 0f ) );

        mesh.SetVertexBlend( hHalfEdge, blend );

        return HandlerBase.Confirm( $"Set vertex {vertexIndex} blend on '{go.Name}'." );
    }

    // ── get_info ──────────────────────────────────────────────────────
    // Ported from MeshEditHandlers.GetMeshInfo

    private static object GetInfo( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "get_info" );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            return HandlerBase.Error( "Missing required 'id' parameter.", "get_info" );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, "get_info" );
        var meshComponent = go.Components.Get<MeshComponent>();
        if ( meshComponent == null )
            return HandlerBase.Error( $"GameObject '{go.Name}' has no MeshComponent.", "get_info" );

        var mesh = meshComponent.Mesh;
        var faceCount = MaterialHelper.GetFaceCount( mesh );
        var vertexCount = MaterialHelper.GetVertexCount( mesh );
        var edgeCount = MaterialHelper.GetEdgeCount( mesh );

        var faceData = new List<object>();
        int idx = 0;
        foreach ( var hFace in mesh.FaceHandles )
        {
            var mat = mesh.GetFaceMaterial( hFace );
            faceData.Add( new
            {
                index = idx++,
                material = mat?.ResourcePath ?? "default",
                material_name = mat?.Name ?? "default"
            } );
        }

        var bounds = mesh.CalculateBounds();

        return HandlerBase.Success( new
        {
            message = $"Mesh info for '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            face_count = faceCount,
            vertex_count = vertexCount,
            edge_count = edgeCount,
            bounds = new
            {
                mins = HandlerBase.V3( bounds.Mins ),
                maxs = HandlerBase.V3( bounds.Maxs )
            },
            faces = faceData
        } );
    }
}
