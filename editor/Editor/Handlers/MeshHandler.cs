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
/// mesh tool: create/edit polygon meshes via PolygonMesh half-edge API.
/// Phases 1-3: primitives (block/plane/cylinder/wedge/arch), construction
/// (extrude/remove/clip/add/scale), refinement (thicken/bevel/split/slice/dissolve),
/// topology (bridge/connect/flip/extend), per-vertex/face editing, get_info.
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
                "create_plane"        => CreatePlane( args ),
                "create_cylinder"     => CreateCylinder( args ),
                "extrude_faces"       => ExtrudeFaces( args ),
                "remove_faces"        => RemoveFaces( args ),
                "add_face"            => AddFace( args ),
                "clip_faces"          => ClipFaces( args ),
                "scale_mesh"          => ScaleMesh( args ),
                "thicken_faces"       => ThickenFaces( args ),
                "bevel_edges"         => BevelEdges( args ),
                "bevel_vertices"      => BevelVertices( args ),
                "split_edges"         => SplitEdges( args ),
                "quad_slice_faces"    => QuadSliceFaces( args ),
                "dissolve_edges"      => DissolveEdges( args ),
                "bridge_edges"        => BridgeEdgesAction( args ),
                "connect_vertices"    => ConnectVerticesAction( args ),
                "flip_faces"          => FlipFaces( args ),
                "extend_edges"        => ExtendEdges( args ),
                "create_wedge"        => CreateWedge( args ),
                "create_arch"         => CreateArch( args ),
                "set_face_material"   => SetFaceMaterial( args ),
                "set_texture_params"  => SetTextureParams( args ),
                "set_vertex_position" => SetVertexPosition( args ),
                "set_vertex_color"    => SetVertexColor( args ),
                "set_vertex_blend"    => SetVertexBlend( args ),
                "get_info"            => GetInfo( args ),
                _ => HandlerBase.Error( $"Unknown action '{action}'", action,
                    "Valid actions: create_block, create_clutter, create_plane, create_cylinder, create_wedge, create_arch, extrude_faces, remove_faces, add_face, clip_faces, scale_mesh, thicken_faces, bevel_edges, bevel_vertices, split_edges, quad_slice_faces, dissolve_edges, bridge_edges, connect_vertices, flip_faces, extend_edges, set_face_material, set_texture_params, set_vertex_position, set_vertex_color, set_vertex_blend, get_info" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action );
        }
    }

    // ── Shared helpers ───────────────────────────────────────────────

    private static (GameObject go, MeshComponent mc, PolygonMesh mesh) ResolveMesh( JsonElement args, string action )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            throw new Exception( "No active scene." );

        var id = HandlerBase.GetString( args, "id" );
        if ( string.IsNullOrEmpty( id ) )
            throw new Exception( $"Missing required 'id' parameter for '{action}'." );

        var go = SceneHelpers.FindByIdOrThrow( scene, id, action );
        var mc = go.Components.Get<MeshComponent>();
        if ( mc == null )
            throw new Exception( $"GameObject '{go.Name}' has no MeshComponent." );

        return (go, mc, mc.Mesh);
    }

    private static FaceHandle[] ParseFaceHandles( PolygonMesh mesh, string indicesStr, string action )
    {
        var indices = HandlerBase.ParseIntArray( indicesStr );
        var handles = new FaceHandle[indices.Length];
        for ( int i = 0; i < indices.Length; i++ )
        {
            handles[i] = mesh.FaceHandleFromIndex( indices[i] );
            if ( !handles[i].IsValid )
                throw new Exception( $"Invalid face index {indices[i]} for '{action}'." );
        }
        return handles;
    }

    private static VertexHandle[] ParseVertexHandles( PolygonMesh mesh, string indicesStr, string action )
    {
        var indices = HandlerBase.ParseIntArray( indicesStr );
        var handles = new VertexHandle[indices.Length];
        for ( int i = 0; i < indices.Length; i++ )
        {
            handles[i] = mesh.VertexHandleFromIndex( indices[i] );
            if ( !handles[i].IsValid )
                throw new Exception( $"Invalid vertex index {indices[i]} for '{action}'." );
        }
        return handles;
    }

    private static HalfEdgeHandle[] ParseEdgeHandles( PolygonMesh mesh, string indicesStr, string action )
    {
        var indices = HandlerBase.ParseIntArray( indicesStr );
        var handles = new HalfEdgeHandle[indices.Length];
        for ( int i = 0; i < indices.Length; i++ )
        {
            handles[i] = mesh.HalfEdgeHandleFromIndex( indices[i] );
            if ( !handles[i].IsValid )
                throw new Exception( $"Invalid edge index {indices[i]} for '{action}'." );
        }
        return handles;
    }

    // ── create_plane ─────────────────────────────────────────────────

    private static object CreatePlane( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_plane" );

        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;

        var sizeStr = HandlerBase.GetString( args, "size" );
        Vector3 size;
        if ( sizeStr != null )
            size = HandlerBase.ParseVector3( sizeStr );
        else
            size = new Vector3( 100, 100, 0 );

        var materialPath = HandlerBase.GetString( args, "material", "materials/dev/reflectivity_30.vmat" );
        var name = HandlerBase.GetString( args, "name" ) ?? "Plane";

        var gameObject = scene.CreateObject();
        gameObject.Name = name;
        gameObject.WorldPosition = position;

        var material = MaterialHelper.LoadMaterialOrDefault( materialPath );

        var hw = size.x / 2f;
        var hh = size.y / 2f;

        var mesh = new PolygonMesh();
        var v = mesh.AddVertices( new Vector3[]
        {
            new( -hw, -hh, 0 ),
            new(  hw, -hh, 0 ),
            new(  hw,  hh, 0 ),
            new( -hw,  hh, 0 ),
        } );

        var hFace = mesh.AddFace( new[] { v[0], v[1], v[2], v[3] } );
        mesh.SetFaceMaterial( hFace, material );
        mesh.SetSmoothingAngle( 40.0f );

        var meshComponent = gameObject.Components.Create<MeshComponent>();
        meshComponent.Mesh = mesh;
        meshComponent.RebuildMesh();

        gameObject.Tags.Add( "mesh" );
        gameObject.Tags.Add( "plane" );

        return HandlerBase.Success( new
        {
            message = $"Created plane '{name}' ({size.x}x{size.y}).",
            id = gameObject.Id.ToString(),
            name = gameObject.Name,
            position = HandlerBase.V3( gameObject.WorldPosition ),
            face_count = 1,
            vertex_count = 4,
            material = materialPath
        } );
    }

    // ── create_cylinder ──────────────────────────────────────────────

    private static object CreateCylinder( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_cylinder" );

        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;

        var sizeStr = HandlerBase.GetString( args, "size" );
        Vector3 size;
        if ( sizeStr != null )
            size = HandlerBase.ParseVector3( sizeStr );
        else
            size = new Vector3( 100, 100, 100 );

        var segments = HandlerBase.GetInt( args, "segments", 16 );
        if ( segments < 3 ) segments = 3;
        if ( segments > 64 ) segments = 64;

        var materialPath = HandlerBase.GetString( args, "material", "materials/dev/reflectivity_30.vmat" );
        var name = HandlerBase.GetString( args, "name" ) ?? "Cylinder";

        var gameObject = scene.CreateObject();
        gameObject.Name = name;
        gameObject.WorldPosition = position;

        var material = MaterialHelper.LoadMaterialOrDefault( materialPath );

        var rx = size.x / 2f;
        var ry = size.y / 2f;
        var hz = size.z / 2f;

        var mesh = new PolygonMesh();

        // Bottom and top ring vertices
        var bottomVerts = new VertexHandle[segments];
        var topVerts = new VertexHandle[segments];
        for ( int i = 0; i < segments; i++ )
        {
            var angle = (float)i / segments * MathF.PI * 2f;
            var x = MathF.Cos( angle ) * rx;
            var y = MathF.Sin( angle ) * ry;
            bottomVerts[i] = mesh.AddVertex( new Vector3( x, y, -hz ) );
            topVerts[i] = mesh.AddVertex( new Vector3( x, y, hz ) );
        }

        // Bottom cap (winding: clockwise from below = CCW looking down from outside)
        var bottomFace = mesh.AddFace( bottomVerts.Reverse().ToArray() );
        mesh.SetFaceMaterial( bottomFace, material );

        // Top cap
        var topFace = mesh.AddFace( topVerts );
        mesh.SetFaceMaterial( topFace, material );

        // Side quads
        int faceCount = 2;
        for ( int i = 0; i < segments; i++ )
        {
            var next = (i + 1) % segments;
            var sideFace = mesh.AddFace( new[]
            {
                bottomVerts[i], bottomVerts[next], topVerts[next], topVerts[i]
            } );
            mesh.SetFaceMaterial( sideFace, material );
            faceCount++;
        }

        mesh.SetSmoothingAngle( 40.0f );

        var meshComponent = gameObject.Components.Create<MeshComponent>();
        meshComponent.Mesh = mesh;
        meshComponent.RebuildMesh();

        gameObject.Tags.Add( "mesh" );
        gameObject.Tags.Add( "cylinder" );

        return HandlerBase.Success( new
        {
            message = $"Created cylinder '{name}' ({size.x}x{size.y}x{size.z}, {segments} segments).",
            id = gameObject.Id.ToString(),
            name = gameObject.Name,
            position = HandlerBase.V3( gameObject.WorldPosition ),
            face_count = faceCount,
            vertex_count = segments * 2,
            segments,
            material = materialPath
        } );
    }

    // ── extrude_faces ────────────────────────────────────────────────

    private static object ExtrudeFaces( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "extrude_faces" );

        var indicesStr = HandlerBase.GetString( args, "face_indices" );
        if ( string.IsNullOrEmpty( indicesStr ) )
            return HandlerBase.Error( "Missing required 'face_indices' parameter.", "extrude_faces" );

        var faceHandles = ParseFaceHandles( mesh, indicesStr, "extrude_faces" );

        // Determine offset: explicit or default to first face normal * 50
        var offsetStr = HandlerBase.GetString( args, "extrude_offset" );
        Vector3 offset;
        if ( offsetStr != null )
        {
            offset = HandlerBase.ParseVector3( offsetStr );
        }
        else
        {
            Vector3 normal = Vector3.Up;
            mesh.ComputeFaceNormal( faceHandles[0], out normal );
            offset = normal * 50f;
        }

        mesh.ExtrudeFaces( faceHandles, out var newFaces, out var connectingFaces, offset );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Extruded {faceHandles.Length} face(s) on '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            new_face_indices = newFaces.Select( f => f.Index ).ToArray(),
            connecting_face_indices = connectingFaces.Select( f => f.Index ).ToArray(),
            offset = HandlerBase.V3( offset )
        } );
    }

    // ── remove_faces ─────────────────────────────────────────────────

    private static object RemoveFaces( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "remove_faces" );

        var indicesStr = HandlerBase.GetString( args, "face_indices" );
        if ( string.IsNullOrEmpty( indicesStr ) )
            return HandlerBase.Error( "Missing required 'face_indices' parameter.", "remove_faces" );

        var faceHandles = ParseFaceHandles( mesh, indicesStr, "remove_faces" );
        var count = faceHandles.Length;

        mesh.RemoveFaces( faceHandles );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Removed {count} face(s) from '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            removed_count = count,
            remaining_faces = MaterialHelper.GetFaceCount( mesh ),
            remaining_vertices = MaterialHelper.GetVertexCount( mesh )
        } );
    }

    // ── add_face ─────────────────────────────────────────────────────

    private static object AddFace( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "add_face" );

        var indicesStr = HandlerBase.GetString( args, "vertex_indices" );
        if ( string.IsNullOrEmpty( indicesStr ) )
            return HandlerBase.Error( "Missing required 'vertex_indices' parameter.", "add_face" );

        var vertexHandles = ParseVertexHandles( mesh, indicesStr, "add_face" );
        if ( vertexHandles.Length < 3 )
            return HandlerBase.Error( "Need at least 3 vertex indices to form a face.", "add_face" );

        var newFace = mesh.AddFace( vertexHandles );
        if ( !newFace.IsValid )
            return HandlerBase.Error( "Failed to create face — vertices may not form valid topology.", "add_face" );

        var materialPath = HandlerBase.GetString( args, "material" );
        if ( !string.IsNullOrEmpty( materialPath ) )
        {
            var material = MaterialHelper.LoadMaterial( materialPath );
            if ( material != null )
                mesh.SetFaceMaterial( newFace, material );
        }

        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Added face with {vertexHandles.Length} vertices to '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            face_index = newFace.Index,
            vertex_count = vertexHandles.Length
        } );
    }

    // ── clip_faces ───────────────────────────────────────────────────

    private static object ClipFaces( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "clip_faces" );

        var indicesStr = HandlerBase.GetString( args, "face_indices" );
        if ( string.IsNullOrEmpty( indicesStr ) )
            return HandlerBase.Error( "Missing required 'face_indices' parameter.", "clip_faces" );

        var normalStr = HandlerBase.GetString( args, "plane_normal" );
        if ( string.IsNullOrEmpty( normalStr ) )
            return HandlerBase.Error( "Missing required 'plane_normal' parameter.", "clip_faces" );

        var pointStr = HandlerBase.GetString( args, "plane_point" );
        if ( string.IsNullOrEmpty( pointStr ) )
            return HandlerBase.Error( "Missing required 'plane_point' parameter.", "clip_faces" );

        var faceHandles = ParseFaceHandles( mesh, indicesStr, "clip_faces" );
        var planeNormal = HandlerBase.ParseVector3( normalStr );
        var planePoint = HandlerBase.ParseVector3( pointStr );
        var removeBehind = HandlerBase.GetBool( args, "remove_behind", true );
        var cap = HandlerBase.GetBool( args, "cap", true );

        var plane = new Plane( planePoint, planeNormal );

        var outNewEdges = new List<HalfEdgeHandle>();
        var outCapFaces = new List<FaceHandle>();
        mesh.ClipFacesByPlaneAndCap( faceHandles, plane, removeBehind, cap, outNewEdges, outCapFaces );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Clipped {faceHandles.Length} face(s) on '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            new_edge_count = outNewEdges.Count,
            cap_face_indices = outCapFaces.Select( f => f.Index ).ToArray(),
            remaining_faces = MaterialHelper.GetFaceCount( mesh )
        } );
    }

    // ── scale_mesh ───────────────────────────────────────────────────

    private static object ScaleMesh( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "scale_mesh" );

        var sizeStr = HandlerBase.GetString( args, "size" );
        if ( string.IsNullOrEmpty( sizeStr ) )
            return HandlerBase.Error( "Missing required 'size' parameter (scale as 'x,y,z').", "scale_mesh" );

        var scale = HandlerBase.ParseVector3( sizeStr );
        mesh.Scale( scale );
        mc.RebuildMesh();

        var bounds = mesh.CalculateBounds();

        return HandlerBase.Success( new
        {
            message = $"Scaled mesh '{go.Name}' by ({sizeStr}).",
            id = go.Id.ToString(),
            name = go.Name,
            scale = HandlerBase.V3( scale ),
            bounds = new
            {
                mins = HandlerBase.V3( bounds.Mins ),
                maxs = HandlerBase.V3( bounds.Maxs )
            }
        } );
    }

    // ── thicken_faces ────────────────────────────────────────────────

    private static object ThickenFaces( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "thicken_faces" );

        var indicesStr = HandlerBase.GetString( args, "face_indices" );
        if ( string.IsNullOrEmpty( indicesStr ) )
            return HandlerBase.Error( "Missing required 'face_indices' parameter.", "thicken_faces" );

        var amount = HandlerBase.GetFloat( args, "amount", 0f );
        if ( amount == 0f )
            return HandlerBase.Error( "Missing or zero 'amount' parameter.", "thicken_faces" );

        var faceHandles = ParseFaceHandles( mesh, indicesStr, "thicken_faces" );
        var faceList = new List<FaceHandle>( faceHandles );

        var success = mesh.ThickenFaces( faceList, amount, out var outFaces );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Thickened {faceHandles.Length} face(s) on '{go.Name}' by {amount}.",
            id = go.Id.ToString(),
            name = go.Name,
            success,
            new_face_indices = outFaces?.Select( f => f.Index ).ToArray() ?? Array.Empty<int>()
        } );
    }

    // ── bevel_edges ──────────────────────────────────────────────────

    private static object BevelEdges( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "bevel_edges" );

        var indicesStr = HandlerBase.GetString( args, "edge_indices" );
        if ( string.IsNullOrEmpty( indicesStr ) )
            return HandlerBase.Error( "Missing required 'edge_indices' parameter.", "bevel_edges" );

        var edgeHandles = ParseEdgeHandles( mesh, indicesStr, "bevel_edges" );
        var edgeList = new List<HalfEdgeHandle>( edgeHandles );
        var distance = HandlerBase.GetFloat( args, "distance", 5f );
        var segments = HandlerBase.GetInt( args, "bevel_segments", 1 );
        var shape = HandlerBase.GetFloat( args, "shape", 0f );

        var outNewFaces = new List<FaceHandle>();
        var success = mesh.BevelEdges( edgeList, PolygonMesh.BevelEdgesMode.RemoveOriginalEdges,
            segments, distance, shape, null, null, outNewFaces );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Beveled {edgeHandles.Length} edge(s) on '{go.Name}' (distance={distance}, segments={segments}).",
            id = go.Id.ToString(),
            name = go.Name,
            success,
            new_face_count = outNewFaces.Count
        } );
    }

    // ── bevel_vertices ───────────────────────────────────────────────

    private static object BevelVertices( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "bevel_vertices" );

        var indicesStr = HandlerBase.GetString( args, "vertex_indices" );
        if ( string.IsNullOrEmpty( indicesStr ) )
            return HandlerBase.Error( "Missing required 'vertex_indices' parameter.", "bevel_vertices" );

        var vertexHandles = ParseVertexHandles( mesh, indicesStr, "bevel_vertices" );
        var vertexList = new List<VertexHandle>( vertexHandles );
        var distance = HandlerBase.GetFloat( args, "distance", 5f );

        var success = mesh.BevelVertices( vertexList, distance, out var outNewVertices );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Beveled {vertexHandles.Length} vertex/vertices on '{go.Name}' (distance={distance}).",
            id = go.Id.ToString(),
            name = go.Name,
            success,
            new_vertex_count = outNewVertices?.Count ?? 0
        } );
    }

    // ── split_edges ──────────────────────────────────────────────────

    private static object SplitEdges( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "split_edges" );

        var indicesStr = HandlerBase.GetString( args, "edge_indices" );
        if ( string.IsNullOrEmpty( indicesStr ) )
            return HandlerBase.Error( "Missing required 'edge_indices' parameter.", "split_edges" );

        var edgeHandles = ParseEdgeHandles( mesh, indicesStr, "split_edges" );
        var edgeList = new List<HalfEdgeHandle>( edgeHandles );

        var success = mesh.SplitEdges( edgeList, out var newEdgesA, out var newEdgesB );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Split {edgeHandles.Length} edge(s) on '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            success,
            new_edges_a_count = newEdgesA?.Length ?? 0,
            new_edges_b_count = newEdgesB?.Length ?? 0
        } );
    }

    // ── quad_slice_faces ─────────────────────────────────────────────

    private static object QuadSliceFaces( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "quad_slice_faces" );

        var indicesStr = HandlerBase.GetString( args, "face_indices" );
        if ( string.IsNullOrEmpty( indicesStr ) )
            return HandlerBase.Error( "Missing required 'face_indices' parameter.", "quad_slice_faces" );

        var faceHandles = ParseFaceHandles( mesh, indicesStr, "quad_slice_faces" );
        var faceList = new List<FaceHandle>( faceHandles );
        var cutsX = HandlerBase.GetInt( args, "cuts_x", 1 );
        var cutsY = HandlerBase.GetInt( args, "cuts_y", 1 );

        var outNewFaces = new List<FaceHandle>();
        mesh.QuadSliceFaces( faceList, cutsX, cutsY, 45f, outNewFaces );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Quad-sliced {faceHandles.Length} face(s) on '{go.Name}' ({cutsX}x{cutsY}).",
            id = go.Id.ToString(),
            name = go.Name,
            new_face_indices = outNewFaces.Select( f => f.Index ).ToArray()
        } );
    }

    // ── dissolve_edges ───────────────────────────────────────────────

    private static object DissolveEdges( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "dissolve_edges" );

        var indicesStr = HandlerBase.GetString( args, "edge_indices" );
        if ( string.IsNullOrEmpty( indicesStr ) )
            return HandlerBase.Error( "Missing required 'edge_indices' parameter.", "dissolve_edges" );

        var edgeHandles = ParseEdgeHandles( mesh, indicesStr, "dissolve_edges" );
        var edgeList = new List<HalfEdgeHandle>( edgeHandles );
        var mustBePlanar = HandlerBase.GetBool( args, "must_be_planar", false );

        mesh.DissolveEdges( edgeList, mustBePlanar, PolygonMesh.DissolveRemoveVertexCondition.Colinear );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Dissolved {edgeHandles.Length} edge(s) on '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            remaining_faces = MaterialHelper.GetFaceCount( mesh ),
            remaining_edges = MaterialHelper.GetEdgeCount( mesh )
        } );
    }

    // ── bridge_edges ─────────────────────────────────────────────────

    private static object BridgeEdgesAction( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "bridge_edges" );

        var idxA = HandlerBase.GetInt( args, "edge_index_a", -1 );
        var idxB = HandlerBase.GetInt( args, "edge_index_b", -1 );
        if ( idxA < 0 || idxB < 0 )
            return HandlerBase.Error( "Missing required 'edge_index_a' and 'edge_index_b' parameters.", "bridge_edges" );

        var edgeA = mesh.HalfEdgeHandleFromIndex( idxA );
        var edgeB = mesh.HalfEdgeHandleFromIndex( idxB );
        if ( !edgeA.IsValid )
            return HandlerBase.Error( $"Invalid edge index {idxA}.", "bridge_edges" );
        if ( !edgeB.IsValid )
            return HandlerBase.Error( $"Invalid edge index {idxB}.", "bridge_edges" );

        var success = mesh.BridgeEdges( edgeA, edgeB, out var newFace );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Bridged edges {idxA} and {idxB} on '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            success,
            new_face_index = newFace.IsValid ? newFace.Index : -1
        } );
    }

    // ── connect_vertices ─────────────────────────────────────────────

    private static object ConnectVerticesAction( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "connect_vertices" );

        var idxA = HandlerBase.GetInt( args, "vertex_index_a", -1 );
        var idxB = HandlerBase.GetInt( args, "vertex_index_b", -1 );
        if ( idxA < 0 || idxB < 0 )
            return HandlerBase.Error( "Missing required 'vertex_index_a' and 'vertex_index_b' parameters.", "connect_vertices" );

        var vertA = mesh.VertexHandleFromIndex( idxA );
        var vertB = mesh.VertexHandleFromIndex( idxB );
        if ( !vertA.IsValid )
            return HandlerBase.Error( $"Invalid vertex index {idxA}.", "connect_vertices" );
        if ( !vertB.IsValid )
            return HandlerBase.Error( $"Invalid vertex index {idxB}.", "connect_vertices" );

        var success = mesh.ConnectVertices( vertA, vertB, out var newEdge );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Connected vertices {idxA} and {idxB} on '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            success,
            new_edge_index = newEdge.IsValid ? newEdge.Index : -1
        } );
    }

    // ── flip_faces ───────────────────────────────────────────────────

    private static object FlipFaces( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "flip_faces" );

        mesh.FlipAllFaces();
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Flipped all face normals on '{go.Name}'.",
            id = go.Id.ToString(),
            name = go.Name,
            face_count = MaterialHelper.GetFaceCount( mesh )
        } );
    }

    // ── extend_edges ─────────────────────────────────────────────────

    private static object ExtendEdges( JsonElement args )
    {
        var (go, mc, mesh) = ResolveMesh( args, "extend_edges" );

        var indicesStr = HandlerBase.GetString( args, "edge_indices" );
        if ( string.IsNullOrEmpty( indicesStr ) )
            return HandlerBase.Error( "Missing required 'edge_indices' parameter.", "extend_edges" );

        var amount = HandlerBase.GetFloat( args, "amount", 0f );
        if ( amount == 0f )
            return HandlerBase.Error( "Missing or zero 'amount' parameter.", "extend_edges" );

        var edgeHandles = ParseEdgeHandles( mesh, indicesStr, "extend_edges" );
        var edgeList = new List<HalfEdgeHandle>( edgeHandles );

        var success = mesh.ExtendEdges( edgeList, amount, out var newEdges, out var newFaces );
        mc.RebuildMesh();

        return HandlerBase.Success( new
        {
            message = $"Extended {edgeHandles.Length} edge(s) on '{go.Name}' by {amount}.",
            id = go.Id.ToString(),
            name = go.Name,
            success,
            new_edge_count = newEdges.Count,
            new_face_count = newFaces.Count
        } );
    }

    // ── create_wedge ─────────────────────────────────────────────────

    private static object CreateWedge( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_wedge" );

        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;

        var sizeStr = HandlerBase.GetString( args, "size" );
        Vector3 size;
        if ( sizeStr != null )
            size = HandlerBase.ParseVector3( sizeStr );
        else
            size = new Vector3( 100, 100, 100 );

        var materialPath = HandlerBase.GetString( args, "material", "materials/dev/reflectivity_30.vmat" );
        var name = HandlerBase.GetString( args, "name" ) ?? "Wedge";

        var gameObject = scene.CreateObject();
        gameObject.Name = name;
        gameObject.WorldPosition = position;

        var material = MaterialHelper.LoadMaterialOrDefault( materialPath );

        var hx = size.x / 2f;
        var hy = size.y / 2f;
        var hz = size.z / 2f;

        // Triangular prism: 6 vertices, 5 faces (2 triangles + 3 quads)
        // Ridge runs along Y axis at top center
        var mesh = new PolygonMesh();
        var v = mesh.AddVertices( new Vector3[]
        {
            new( -hx, -hy, -hz ), // 0: left-front-bottom
            new(  hx, -hy, -hz ), // 1: right-front-bottom
            new(  hx,  hy, -hz ), // 2: right-back-bottom
            new( -hx,  hy, -hz ), // 3: left-back-bottom
            new(   0, -hy,  hz ), // 4: front-top-center (ridge)
            new(   0,  hy,  hz ), // 5: back-top-center (ridge)
        } );

        var faceIndices = new[]
        {
            new[] { 3, 2, 1, 0 },    // Bottom quad
            new[] { 0, 1, 4 },        // Front triangle
            new[] { 5, 2, 3 },        // Back triangle
            new[] { 0, 4, 5, 3 },     // Left slope
            new[] { 1, 2, 5, 4 },     // Right slope
        };

        int faceCount = 0;
        foreach ( var fi in faceIndices )
        {
            var handles = new VertexHandle[fi.Length];
            for ( int i = 0; i < fi.Length; i++ )
                handles[i] = v[fi[i]];
            var hFace = mesh.AddFace( handles );
            mesh.SetFaceMaterial( hFace, material );
            faceCount++;
        }

        mesh.SetSmoothingAngle( 40.0f );

        var meshComponent = gameObject.Components.Create<MeshComponent>();
        meshComponent.Mesh = mesh;
        meshComponent.RebuildMesh();

        gameObject.Tags.Add( "mesh" );
        gameObject.Tags.Add( "wedge" );

        return HandlerBase.Success( new
        {
            message = $"Created wedge '{name}' ({size.x}x{size.y}x{size.z}).",
            id = gameObject.Id.ToString(),
            name = gameObject.Name,
            position = HandlerBase.V3( gameObject.WorldPosition ),
            face_count = faceCount,
            vertex_count = 6,
            material = materialPath
        } );
    }

    // ── create_arch ──────────────────────────────────────────────────

    private static object CreateArch( JsonElement args )
    {
        var scene = SceneHelpers.ResolveScene();
        if ( scene == null )
            return HandlerBase.Error( "No active scene.", "create_arch" );

        var posStr = HandlerBase.GetString( args, "position" );
        var position = posStr != null ? HandlerBase.ParseVector3( posStr ) : Vector3.Zero;

        var sizeStr = HandlerBase.GetString( args, "size" );
        Vector3 size;
        if ( sizeStr != null )
            size = HandlerBase.ParseVector3( sizeStr );
        else
            size = new Vector3( 100, 50, 100 );

        var segments = HandlerBase.GetInt( args, "segments", 8 );
        if ( segments < 3 ) segments = 3;
        if ( segments > 32 ) segments = 32;

        var materialPath = HandlerBase.GetString( args, "material", "materials/dev/reflectivity_30.vmat" );
        var name = HandlerBase.GetString( args, "name" ) ?? "Arch";

        var gameObject = scene.CreateObject();
        gameObject.Name = name;
        gameObject.WorldPosition = position;

        var material = MaterialHelper.LoadMaterialOrDefault( materialPath );

        var hw = size.x / 2f; // half width
        var hd = size.y / 2f; // half depth
        var h  = size.z;      // full height

        var mesh = new PolygonMesh();

        // Generate arch vertices: semicircle from left to right
        // Front and back rings of vertices along the arch
        var frontVerts = new VertexHandle[segments + 1];
        var backVerts = new VertexHandle[segments + 1];

        for ( int i = 0; i <= segments; i++ )
        {
            var t = (float)i / segments;
            var angle = MathF.PI * t; // 0 to PI (left to right)
            var x = -MathF.Cos( angle ) * hw;
            var z = MathF.Sin( angle ) * h;
            frontVerts[i] = mesh.AddVertex( new Vector3( x, -hd, z ) );
            backVerts[i] = mesh.AddVertex( new Vector3( x, hd, z ) );
        }

        int faceCount = 0;

        // Outer surface quads
        for ( int i = 0; i < segments; i++ )
        {
            var face = mesh.AddFace( new[] { frontVerts[i], frontVerts[i + 1], backVerts[i + 1], backVerts[i] } );
            mesh.SetFaceMaterial( face, material );
            faceCount++;
        }

        // Side cap faces (front and back — fan triangulation)
        // Front side
        for ( int i = 1; i < segments; i++ )
        {
            var face = mesh.AddFace( new[] { frontVerts[0], frontVerts[i], frontVerts[i + 1] } );
            mesh.SetFaceMaterial( face, material );
            faceCount++;
        }

        // Back side (reversed winding)
        for ( int i = 1; i < segments; i++ )
        {
            var face = mesh.AddFace( new[] { backVerts[0], backVerts[i + 1], backVerts[i] } );
            mesh.SetFaceMaterial( face, material );
            faceCount++;
        }

        mesh.SetSmoothingAngle( 40.0f );

        var meshComponent = gameObject.Components.Create<MeshComponent>();
        meshComponent.Mesh = mesh;
        meshComponent.RebuildMesh();

        gameObject.Tags.Add( "mesh" );
        gameObject.Tags.Add( "arch" );

        return HandlerBase.Success( new
        {
            message = $"Created arch '{name}' ({size.x}x{size.y}x{size.z}, {segments} segments).",
            id = gameObject.Id.ToString(),
            name = gameObject.Name,
            position = HandlerBase.V3( gameObject.WorldPosition ),
            face_count = faceCount,
            vertex_count = (segments + 1) * 2,
            segments,
            material = materialPath
        } );
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

        var hx = size.x / 2f;
        var hy = size.y / 2f;
        var hz = size.z / 2f;

        // 8 shared vertices — required for valid half-edge mesh topology
        var mesh = new PolygonMesh();
        var v = mesh.AddVertices( new Vector3[]
        {
            new( -hx, -hy, -hz ), // 0: left-front-bottom
            new(  hx, -hy, -hz ), // 1: right-front-bottom
            new(  hx,  hy, -hz ), // 2: right-back-bottom
            new( -hx,  hy, -hz ), // 3: left-back-bottom
            new( -hx, -hy,  hz ), // 4: left-front-top
            new(  hx, -hy,  hz ), // 5: right-front-top
            new(  hx,  hy,  hz ), // 6: right-back-top
            new( -hx,  hy,  hz ), // 7: left-back-top
        } );

        // 6 quad faces with outward-facing winding (CCW from outside)
        var faceIndices = new[]
        {
            new[] { 4, 5, 6, 7 }, // Top (+Z)
            new[] { 3, 2, 1, 0 }, // Bottom (-Z)
            new[] { 0, 1, 5, 4 }, // Front (-Y)
            new[] { 7, 6, 2, 3 }, // Back (+Y)
            new[] { 1, 2, 6, 5 }, // Right (+X)
            new[] { 4, 7, 3, 0 }, // Left (-X)
        };

        var hFaces = new List<FaceHandle>();
        foreach ( var fi in faceIndices )
        {
            var hFace = mesh.AddFace( new[] { v[fi[0]], v[fi[1]], v[fi[2]], v[fi[3]] } );
            mesh.SetFaceMaterial( hFace, material );
            hFaces.Add( hFace );
        }

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
            vertex_count = 8,
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
            if ( asset == null )
                throw new InvalidOperationException(
                    $"ClutterDefinition asset not found at '{defPath}'. User-created .clutter files must live under 'Assets/' (path relative to Assets/, no 'Assets/' prefix)." );
            var def = asset.LoadResource<ClutterDefinition>();
            if ( def == null )
                throw new InvalidOperationException(
                    $"Asset at '{defPath}' exists but is not a ClutterDefinition or failed to load." );
            clutter.Clutter = def;
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
