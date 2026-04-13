using System.Collections.Generic;

namespace Arenula;

/// <summary>
/// All 19 Arenula tool schemas for the MCP tools/list response.
/// Each tool uses the omnibus pattern: required action enum + flat params.
/// Descriptions follow Anthropic best practices: 3-4 sentences, negative guidance.
/// </summary>
internal static class ToolRegistry
{
    internal static readonly string Version = "1.0.0";

    internal static object[] All => new object[]
    {
        Scene, GameObject, Component, Prefab,
        AssetQuery, AssetManage, Editor, Compile,
        Mesh, Lighting, Physics, Audio,
        Effects, Camera, Navmesh, Session,
        Project, Cloud, Terrain
    };

    // ── Tool 1: scene ────────────────────────────────────────────────

    internal static readonly object Scene = new
    {
        name = "scene",
        description = "Read and query the s&box editor scene. Use this to understand what's in the scene before making changes. Actions: 'summary' returns a high-level overview with root object list, 'hierarchy' returns the full GameObject tree, 'statistics' returns object/component counts, 'find' searches by name/tag/component type with pagination, 'find_in_radius' searches spatially around a point, 'get_details' returns full info on a specific GameObject including all components, 'prefab_instances' lists all prefab instances. This tool only reads scene state — use 'gameobject' or 'component' to create or modify objects.",
        annotations = new { readOnlyHint = true },
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "summary", "hierarchy", "statistics", "find", "find_in_radius", "get_details", "prefab_instances" } },
                ["id"] = new { type = "string", description = "GameObject GUID. Required for: get_details." },
                ["query"] = new { type = "string", description = "Search term. Used by: find." },
                ["tag"] = new { type = "string", description = "Tag filter. Used by: find." },
                ["component_type"] = new { type = "string", description = "Component type filter. Used by: find." },
                ["position"] = new { type = "string", description = "Center point as 'x,y,z'. Required for: find_in_radius." },
                ["radius"] = new { type = "number", description = "Search radius in units. Required for: find_in_radius." },
                ["prefab_path"] = new { type = "string", description = "Filter by prefab source path. Used by: prefab_instances." },
                ["max_depth"] = new { type = "integer", description = "Max tree depth. Used by: hierarchy. Default: unlimited." },
                ["offset"] = new { type = "integer", description = "Pagination offset. Used by: find, find_in_radius, prefab_instances. Default: 0." },
                ["limit"] = new { type = "integer", description = "Max results. Used by: find, find_in_radius, prefab_instances. Default: 50." },
                ["format"] = new { type = "string", description = "Response detail level. Used by: find, get_details. Values: 'concise' (default), 'detailed'.", @enum = new[] { "concise", "detailed" } }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 2: gameobject ───────────────────────────────────────────

    internal static readonly object GameObject = new
    {
        name = "gameobject",
        description = "Create, modify, and destroy GameObjects in the scene. Use 'scene' to find objects first, then this tool to change them. For adding or configuring components, use 'component' instead. All write actions return the GUID and name of the affected object. Use 'batch_transform' to move multiple objects in a single call.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "create", "destroy", "duplicate", "reparent", "rename", "enable", "set_tags", "set_transform", "batch_transform" } },
                ["id"] = new { type = "string", description = "Target GameObject GUID. Required for: destroy, duplicate, reparent, rename, enable, set_tags, set_transform." },
                ["ids"] = new { type = "string", description = "Comma-separated GUIDs. Required for: batch_transform." },
                ["name"] = new { type = "string", description = "Name for the object. Used by: create, rename." },
                ["parent_id"] = new { type = "string", description = "Parent GUID. Used by: create (optional), reparent (required)." },
                ["position"] = new { type = "string", description = "Position as 'x,y,z'. Used by: create, set_transform, batch_transform." },
                ["rotation"] = new { type = "string", description = "Rotation as 'pitch,yaw,roll'. Used by: create, set_transform, batch_transform." },
                ["scale"] = new { type = "string", description = "Scale as 'x,y,z'. Used by: create, set_transform, batch_transform." },
                ["space"] = new { type = "string", description = "Coordinate space. Used by: set_transform. Values: 'world' (default), 'local'.", @enum = new[] { "world", "local" } },
                ["enabled"] = new { type = "boolean", description = "Enable/disable state. Required for: enable." },
                ["tags"] = new { type = "string", description = "Comma-separated tags. Required for: set_tags." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 3: component ────────────────────────────────────────────

    internal static readonly object Component = new
    {
        name = "component",
        description = "Manage components on GameObjects. Use 'scene' to find objects, then this tool to add, remove, or configure their components. 'add' returns similar type suggestions if the exact name doesn't match. Use 'get_types' with a filter to discover available component types before adding. Use 'get_properties' to inspect a component's current values before setting them.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "add", "remove", "set_property", "set_enabled", "get_properties", "get_types", "copy" } },
                ["id"] = new { type = "string", description = "Target GameObject GUID. Required for: add, remove, set_property, set_enabled, get_properties." },
                ["component_id"] = new { type = "string", description = "Component GUID on the target object. Required for: remove, set_property, set_enabled, get_properties." },
                ["type"] = new { type = "string", description = "Component type name (e.g. 'Rigidbody', 'ModelRenderer'). Required for: add." },
                ["property"] = new { type = "string", description = "Property name. Required for: set_property." },
                ["value"] = new { type = "string", description = "Property value (auto-converted to correct type). Required for: set_property." },
                ["enabled"] = new { type = "boolean", description = "Enable/disable state. Required for: set_enabled." },
                ["source_component_id"] = new { type = "string", description = "Source component GUID to copy from. Required for: copy." },
                ["target_id"] = new { type = "string", description = "Target GameObject GUID to copy to. Required for: copy." },
                ["filter"] = new { type = "string", description = "Type name filter. Used by: get_types." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 4: prefab ───────────────────────────────────────────────

    internal static readonly object Prefab = new
    {
        name = "prefab",
        description = "Manage prefabs: instantiate into scenes, create from GameObjects, and manage instance overrides. Use 'create' to convert a scene GameObject into a reusable .prefab file on disk. Use 'save_overrides' to push instance changes back to the prefab source file. Use 'revert' to discard instance changes and match the source prefab. Use 'get_overrides' to see which properties differ from the source.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "instantiate", "get_structure", "get_instances", "break", "update", "create", "save_overrides", "revert", "get_overrides" } },
                ["id"] = new { type = "string", description = "GameObject GUID. Required for: break, update, create, save_overrides, revert, get_overrides." },
                ["path"] = new { type = "string", description = "Prefab file path. Required for: instantiate, get_structure. Optional filter for: get_instances." },
                ["save_path"] = new { type = "string", description = "Where to save the new prefab file. Required for: create." },
                ["position"] = new { type = "string", description = "Spawn position as 'x,y,z'. Used by: instantiate." },
                ["rotation"] = new { type = "string", description = "Spawn rotation as 'pitch,yaw,roll'. Used by: instantiate." },
                ["offset"] = new { type = "integer", description = "Pagination offset. Used by: get_instances. Default: 0." },
                ["limit"] = new { type = "integer", description = "Max results. Used by: get_instances. Default: 50." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 5: asset_query ──────────────────────────────────────────

    internal static readonly object AssetQuery = new
    {
        name = "asset_query",
        description = "Browse, search, and inspect project assets. Use 'browse' to list a directory, 'search' to find assets by name or type, and the various 'get_*' actions to inspect specific asset details. This tool only reads — use 'asset_manage' to create, rename, move, or delete assets. For cloud/workshop assets, use 'cloud' instead.",
        annotations = new { readOnlyHint = true },
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "browse", "search", "open", "get_dependencies", "get_model_info", "get_material_properties", "get_mesh_info", "get_bounds", "get_unsaved", "get_status", "get_json", "get_references" } },
                ["path"] = new { type = "string", description = "Asset path. Required for: open, get_dependencies, get_model_info, get_material_properties, get_status, get_json, get_references." },
                ["id"] = new { type = "string", description = "GameObject GUID. Required for: get_mesh_info, get_bounds." },
                ["query"] = new { type = "string", description = "Search term. Required for: search." },
                ["type"] = new { type = "string", description = "Asset type filter. Used by: search." },
                ["directory"] = new { type = "string", description = "Directory to browse. Used by: browse." },
                ["deep"] = new { type = "boolean", description = "Include transitive references. Used by: get_references. Default: false." },
                ["format"] = new { type = "string", description = "Response detail level. Used by: search, get_model_info. Values: 'concise' (default), 'detailed'.", @enum = new[] { "concise", "detailed" } },
                ["offset"] = new { type = "integer", description = "Pagination offset. Used by: browse, search. Default: 0." },
                ["limit"] = new { type = "integer", description = "Max results. Used by: browse, search. Default: 50." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 6: asset_manage ─────────────────────────────────────────

    internal static readonly object AssetManage = new
    {
        name = "asset_manage",
        description = "Create, rename, move, delete, save, and reload project assets. Use 'create' to make a new GameResource of any type (material, sound event, etc.). Use 'reload' to force-recompile an asset from disk. Destructive actions (delete) send files to the recycle bin, not permanent deletion. For browsing or inspecting assets, use 'asset_query' instead.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "create", "delete", "rename", "move", "save", "reload", "get_references" } },
                ["path"] = new { type = "string", description = "Source asset path. Required for: delete, rename, move, save, reload, get_references." },
                ["type"] = new { type = "string", description = "GameResource type name. Required for: create." },
                ["new_name"] = new { type = "string", description = "New name for the asset. Required for: rename." },
                ["destination"] = new { type = "string", description = "Target directory path. Required for: move." },
                ["deep"] = new { type = "boolean", description = "Include transitive references. Used by: get_references. Default: false." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 7: editor ───────────────────────────────────────────────

    internal static readonly object Editor = new
    {
        name = "editor",
        description = "Control the s&box editor: manage object selection, play mode, scene saving, undo/redo, console commands, code editor, and preferences. For scene content operations use 'scene' and 'gameobject'. Use 'get_log' to read compiler output and runtime errors. Use 'undo' and 'redo' to step through edit history. Use 'console_run' to execute engine console commands. Console commands execute on a separate dispatch path for reliable error handling.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "select", "get_selected", "set_selected", "clear_selection", "frame_selection", "get_play_state", "start_play", "stop_play", "get_log", "save_scene", "save_scene_as", "undo", "redo", "console_list", "console_run", "open_code_file", "get_preferences", "set_preference" } },
                ["id"] = new { type = "string", description = "GameObject GUID. Used by: select, frame_selection." },
                ["ids"] = new { type = "string", description = "Comma-separated GUIDs. Required for: set_selected." },
                ["path"] = new { type = "string", description = "File or scene path. Required for: save_scene_as, open_code_file." },
                ["command"] = new { type = "string", description = "Console command string. Required for: console_run." },
                ["filter"] = new { type = "string", description = "Filter text. Used by: get_log, console_list." },
                ["count"] = new { type = "integer", description = "Number of log entries. Used by: get_log." },
                ["line"] = new { type = "integer", description = "Line number. Used by: open_code_file." },
                ["column"] = new { type = "integer", description = "Column number. Used by: open_code_file." },
                ["key"] = new { type = "string", description = "Preference key. Required for: set_preference." },
                ["value"] = new { type = "string", description = "Preference value. Required for: set_preference." },
                ["offset"] = new { type = "integer", description = "Pagination offset. Used by: get_log. Default: 0." },
                ["limit"] = new { type = "integer", description = "Max results. Used by: get_log. Default: 50." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 8: compile ──────────────────────────────────────────────

    internal static readonly object Compile = new
    {
        name = "compile",
        description = "Trigger code compilation and read build results. Use 'trigger' to start a build, 'status' to check if compilation is in progress, and 'errors' to read diagnostics with file paths and line numbers. Use 'wait' to block until compilation finishes (with configurable timeout). Essential for validating C# code changes without switching to the editor. Runs on background thread — does not block the editor.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "trigger", "status", "errors", "generate_solution", "wait" } },
                ["severity"] = new { type = "string", description = "Filter diagnostics by severity. Used by: errors. Values: 'error', 'warning', 'info'.", @enum = new[] { "error", "warning", "info" } },
                ["timeout_ms"] = new { type = "integer", description = "Max wait time in milliseconds. Used by: wait. Default: 60000." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 9: mesh ─────────────────────────────────────────────────

    internal static readonly object Mesh = new
    {
        name = "mesh",
        description = "Create and edit polygon meshes directly in the scene. Primitives: 'create_block' (box), 'create_plane' (flat quad), 'create_cylinder' (ring-based), 'create_wedge' (triangular prism/ramp), 'create_arch' (semicircular arc), 'create_clutter' (scattered objects). Construction: 'extrude_faces' (push faces outward), 'remove_faces' (cut holes), 'add_face' (polygon from vertices), 'clip_faces' (slice with plane), 'scale_mesh' (resize). Refinement: 'thicken_faces' (give faces depth), 'bevel_edges' (round edges), 'bevel_vertices' (round corners), 'split_edges' (subdivide edges), 'quad_slice_faces' (subdivide faces into grid), 'dissolve_edges' (simplify by merging faces). Topology: 'bridge_edges' (connect two open edges), 'connect_vertices' (split face by connecting two vertices), 'flip_faces' (reverse normals), 'extend_edges' (extrude boundary edges outward). Per-element: vertex position/color/blend, face material/texture. Note: after topology changes, face/vertex/edge indices are invalidated — use returned indices or 'get_info' for subsequent operations.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "create_block", "create_clutter", "create_plane", "create_cylinder", "create_wedge", "create_arch", "extrude_faces", "remove_faces", "add_face", "clip_faces", "scale_mesh", "thicken_faces", "bevel_edges", "bevel_vertices", "split_edges", "quad_slice_faces", "dissolve_edges", "bridge_edges", "connect_vertices", "flip_faces", "extend_edges", "set_face_material", "set_texture_params", "set_vertex_position", "set_vertex_color", "set_vertex_blend", "get_info" } },
                ["id"] = new { type = "string", description = "Target mesh GameObject GUID. Required for: extrude_faces, remove_faces, add_face, clip_faces, scale_mesh, thicken_faces, bevel_edges, bevel_vertices, split_edges, quad_slice_faces, dissolve_edges, bridge_edges, connect_vertices, flip_faces, extend_edges, set_face_material, set_texture_params, set_vertex_position, set_vertex_color, set_vertex_blend, get_info." },
                ["position"] = new { type = "string", description = "Position as 'x,y,z'. Used by: create_block, create_plane, create_cylinder, create_wedge, create_arch, create_clutter." },
                ["size"] = new { type = "string", description = "Size as 'x,y,z'. Used by: create_block, create_plane, create_cylinder, create_wedge, create_arch. For scale_mesh: scale factor as 'x,y,z'." },
                ["material"] = new { type = "string", description = "Material path. Used by: create_block, create_plane, create_cylinder, create_wedge, create_arch, set_face_material, add_face." },
                ["name"] = new { type = "string", description = "Object name. Used by: create_block, create_plane, create_cylinder, create_wedge, create_arch, create_clutter." },
                ["definition"] = new { type = "string", description = "Clutter definition asset path. Required for: create_clutter." },
                ["radius"] = new { type = "number", description = "Scatter radius. Used by: create_clutter." },
                ["segments"] = new { type = "integer", description = "Segment count (3-64). Used by: create_cylinder (default 16), create_arch (default 8)." },
                ["face_index"] = new { type = "integer", description = "Single face index. Required for: set_face_material, set_texture_params." },
                ["face_indices"] = new { type = "string", description = "Comma-separated face indices '0,1,2'. Required for: extrude_faces, remove_faces, clip_faces, thicken_faces, quad_slice_faces." },
                ["vertex_index"] = new { type = "integer", description = "Single vertex index. Required for: set_vertex_position, set_vertex_color, set_vertex_blend." },
                ["vertex_indices"] = new { type = "string", description = "Comma-separated vertex indices in winding order. Required for: add_face. Used by: bevel_vertices." },
                ["extrude_offset"] = new { type = "string", description = "Extrusion direction/distance as 'x,y,z'. Used by: extrude_faces. Default: face normal * 50." },
                ["plane_normal"] = new { type = "string", description = "Cutting plane normal as 'x,y,z'. Required for: clip_faces." },
                ["plane_point"] = new { type = "string", description = "Point on cutting plane as 'x,y,z'. Required for: clip_faces." },
                ["remove_behind"] = new { type = "boolean", description = "Remove faces behind cutting plane. Used by: clip_faces. Default: true." },
                ["cap"] = new { type = "boolean", description = "Cap the cut opening with a new face. Used by: clip_faces. Default: true." },
                ["edge_indices"] = new { type = "string", description = "Comma-separated half-edge indices. Required for: bevel_edges, split_edges, dissolve_edges, extend_edges." },
                ["edge_index_a"] = new { type = "integer", description = "First edge index. Required for: bridge_edges." },
                ["edge_index_b"] = new { type = "integer", description = "Second edge index. Required for: bridge_edges." },
                ["vertex_index_a"] = new { type = "integer", description = "First vertex index. Required for: connect_vertices." },
                ["vertex_index_b"] = new { type = "integer", description = "Second vertex index. Required for: connect_vertices." },
                ["amount"] = new { type = "number", description = "Thickness/distance amount. Required for: thicken_faces, extend_edges." },
                ["distance"] = new { type = "number", description = "Bevel distance. Used by: bevel_edges, bevel_vertices. Default: 5." },
                ["bevel_segments"] = new { type = "integer", description = "Bevel smoothness (segment count). Used by: bevel_edges. Default: 1." },
                ["shape"] = new { type = "number", description = "Bevel profile shape (-1 to 1). Used by: bevel_edges. Default: 0." },
                ["cuts_x"] = new { type = "integer", description = "X subdivisions. Used by: quad_slice_faces. Default: 1." },
                ["cuts_y"] = new { type = "integer", description = "Y subdivisions. Used by: quad_slice_faces. Default: 1." },
                ["must_be_planar"] = new { type = "boolean", description = "Require planar faces for dissolve. Used by: dissolve_edges. Default: false." },
                ["color"] = new { type = "string", description = "Color as 'r,g,b,a'. Required for: set_vertex_color." },
                ["blend"] = new { type = "number", description = "Blend weight 0-1. Required for: set_vertex_blend." },
                ["offset"] = new { type = "string", description = "Texture offset as 'u,v'. Used by: set_texture_params." },
                ["scale"] = new { type = "string", description = "Texture scale as 'u,v'. Used by: set_texture_params." },
                ["rotation"] = new { type = "number", description = "Texture rotation degrees. Used by: set_texture_params." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 10: lighting ────────────────────────────────────────────

    internal static readonly object Lighting = new
    {
        name = "lighting",
        description = "Create and configure all light types in the scene. Use 'create' with the 'type' parameter: point (omnidirectional), spot (cone), ambient (scene-wide flat), environment (directional sun/moon), or indirect_volume (GI bounce probe). Use 'configure' to modify color, intensity, range, and shadow settings on existing lights. Skybox actions control the scene's background environment and ambient lighting.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "create", "configure", "create_skybox", "set_skybox" } },
                ["id"] = new { type = "string", description = "Light GameObject GUID. Required for: configure. Optional for: set_skybox (auto-finds if omitted)." },
                ["type"] = new { type = "string", description = "Light type. Required for: create.", @enum = new[] { "point", "spot", "ambient", "environment", "indirect_volume" } },
                ["position"] = new { type = "string", description = "Position as 'x,y,z'. Used by: create." },
                ["color"] = new { type = "string", description = "Color as 'r,g,b' (0-255). Used by: create, configure." },
                ["intensity"] = new { type = "number", description = "Light intensity. Used by: create, configure." },
                ["range"] = new { type = "number", description = "Light range in units. Used by: create, configure." },
                ["shadows"] = new { type = "boolean", description = "Enable shadows. Used by: configure." },
                ["material"] = new { type = "string", description = "Skybox material path. Used by: create_skybox, set_skybox." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 11: physics ─────────────────────────────────────────────

    internal static readonly object Physics = new
    {
        name = "physics",
        description = "Add and configure physics components: colliders, rigidbodies, character controllers, and joints. Use 'add_collider' with a 'type' parameter for any shape: box, sphere, capsule, hull (convex), plane (finite quad — default 50x50, NOT infinite), or model (auto-generated from mesh). Use 'create_joint' for physics constraints between objects: fixed, spring, hinge, ball, or slider. Plane colliders are finite meshes — for large ground planes, use a BoxCollider with large dimensions instead.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "add_collider", "configure_collider", "add_rigidbody", "create_model_physics", "create_character_controller", "create_joint" } },
                ["id"] = new { type = "string", description = "Target GameObject GUID. Required for: add_collider, configure_collider, add_rigidbody, create_model_physics, create_character_controller." },
                ["type"] = new { type = "string", description = "Collider or joint type. Required for: add_collider, create_joint." },
                ["component_id"] = new { type = "string", description = "Collider component GUID. Required for: configure_collider." },
                ["size"] = new { type = "string", description = "Collider size as 'x,y,z'. Used by: add_collider." },
                ["center"] = new { type = "string", description = "Collider center offset as 'x,y,z'. Used by: add_collider." },
                ["is_trigger"] = new { type = "boolean", description = "Set as trigger (no physics response). Used by: configure_collider." },
                ["surface"] = new { type = "string", description = "Surface material path. Used by: configure_collider." },
                ["gravity"] = new { type = "boolean", description = "Enable gravity. Used by: add_rigidbody. Default: true." },
                ["mass"] = new { type = "number", description = "Mass in kg. Used by: add_rigidbody." },
                ["enhanced_ccd"] = new { type = "boolean", description = "Enable enhanced continuous collision detection for fast-moving objects (bullets, rockets). Used by: add_rigidbody." },
                ["body_a"] = new { type = "string", description = "First body GUID. Required for: create_joint." },
                ["body_b"] = new { type = "string", description = "Second body GUID. Used by: create_joint." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 12: audio ───────────────────────────────────────────────

    internal static readonly object Audio = new
    {
        name = "audio",
        description = "Create and configure audio components in the scene. Use 'create' with the 'type' parameter: point (directional emitter with falloff), box (rectangular sound zone), soundscape (ambient trigger volume), dsp_volume (audio effect zone), or listener (required for audio playback — every scene needs one). Use 'configure' to adjust volume, radius, falloff, and sound event. For playing sounds at runtime from C# code, use Sound.Play() — this tool is for scene-placed audio sources.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "create", "configure" } },
                ["id"] = new { type = "string", description = "Target GameObject GUID. Required for: configure." },
                ["component_id"] = new { type = "string", description = "Audio component GUID. Required for: configure." },
                ["type"] = new { type = "string", description = "Audio component type. Required for: create.", @enum = new[] { "point", "box", "soundscape", "dsp_volume", "listener" } },
                ["position"] = new { type = "string", description = "Position as 'x,y,z'. Used by: create." },
                ["sound_event"] = new { type = "string", description = "Sound event path. Used by: create." },
                ["volume"] = new { type = "number", description = "Volume 0-1. Used by: configure." },
                ["radius"] = new { type = "number", description = "Sound radius. Used by: configure." },
                ["falloff"] = new { type = "number", description = "Falloff distance. Used by: configure." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 13: effects ─────────────────────────────────────────────

    internal static readonly object Effects = new
    {
        name = "effects",
        description = "Create and configure visual effects in the scene. Use 'create' with the 'type' parameter: particle (particle system), fog (volumetric fog zone), beam (laser/beam between points), rope (verlet physics rope), radius_damage (area damage trigger), or render_entity (custom render object). Use 'configure_particle' or 'configure_post_processing' to modify effect-specific properties. This tool creates scene-placed effects — for triggering effects at runtime from code, use the particle/sound APIs directly.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "create", "configure_particle", "configure_post_processing" } },
                ["id"] = new { type = "string", description = "Target GameObject GUID. Required for: configure_particle, configure_post_processing." },
                ["component_id"] = new { type = "string", description = "Effect component GUID. Required for: configure_particle." },
                ["type"] = new { type = "string", description = "Effect type. Required for: create.", @enum = new[] { "particle", "fog", "beam", "rope", "radius_damage", "render_entity" } },
                ["position"] = new { type = "string", description = "Position as 'x,y,z'. Used by: create." },
                ["properties"] = new { type = "string", description = "JSON object of effect-specific properties. Used by: create, configure_particle, configure_post_processing." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 14: camera ──────────────────────────────────────────────

    internal static readonly object Camera = new
    {
        name = "camera",
        description = "Create and configure camera components, or capture screenshots of the scene. 'capture_viewport': single shot from any position/look_at. 'capture_tour': batch multiple named shots in one call — pass a 'shots' array, get all images back at once. 'orbit_capture': auto-orbit a target point at a given radius/elevation, evenly-spaced shots with compass labels — no coordinate math needed.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "create", "configure", "capture_viewport", "capture_tour", "orbit_capture" } },
                ["id"] = new { type = "string", description = "Camera GameObject GUID. Required for: configure." },
                ["position"] = new { type = "string", description = "Position as 'x,y,z'. Used by: create, capture_viewport." },
                ["rotation"] = new { type = "string", description = "Rotation as 'pitch,yaw,roll'. Used by: create, capture_viewport. Ignored if look_at is set." },
                ["look_at"] = new { type = "string", description = "Target position as 'x,y,z' to aim the camera at. Used by: capture_viewport. Takes priority over rotation." },
                ["fov"] = new { type = "number", description = "Field of view in degrees. Used by: configure, capture_viewport, capture_tour (default 90), orbit_capture (default 75)." },
                ["near_clip"] = new { type = "number", description = "Near clipping plane distance. Used by: configure." },
                ["far_clip"] = new { type = "number", description = "Far clipping plane distance. Used by: configure." },
                ["width"] = new { type = "integer", description = "Capture width in pixels (320-3840). Default 1280. Used by: capture_viewport, capture_tour, orbit_capture." },
                ["height"] = new { type = "integer", description = "Capture height in pixels (240-2160). Default 720. Used by: capture_viewport, capture_tour, orbit_capture." },
                ["quality"] = new { type = "integer", description = "JPEG quality 10-100. Default 75. Used by: capture_viewport, capture_tour, orbit_capture." },
                ["shots"] = new
                {
                    type = "array",
                    description = "Array of shots for capture_tour. Each element: {position: 'x,y,z', look_at: 'x,y,z', label?: 'string', fov?: number}. Shared width/height/quality/fov apply to all shots unless overridden per-shot.",
                    items = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["position"] = new { type = "string", description = "Camera world position as 'x,y,z'." },
                            ["look_at"]  = new { type = "string", description = "Look-at world position as 'x,y,z'." },
                            ["label"]    = new { type = "string", description = "Label shown in the image caption." },
                            ["fov"]      = new { type = "number", description = "Per-shot FOV override." }
                        }
                    }
                },
                ["target"] = new { type = "string", description = "World position as 'x,y,z' to orbit around. Required for: orbit_capture." },
                ["radius"] = new { type = "number", description = "Orbit radius in world units. Default 300. Used by: orbit_capture." },
                ["elevation"] = new { type = "number", description = "Camera world Z height during orbit. Default 200. Used by: orbit_capture." },
                ["count"] = new { type = "integer", description = "Number of evenly-spaced orbit shots (2-32). Default 8. Used by: orbit_capture." },
                ["start_angle"] = new { type = "number", description = "Starting angle in degrees (0 = east). Default 0. Used by: orbit_capture." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 15: navmesh ─────────────────────────────────────────────

    internal static readonly object Navmesh = new
    {
        name = "navmesh",
        description = "Create and manage AI navigation meshes. Place 'create_area' volumes to define walkable regions, 'create_link' for off-mesh connections (jumps, ladders), and 'create_agent' on GameObjects that need pathfinding. Use 'generate' to build the navmesh after placing areas — this can take several seconds on large scenes. Use 'query_path' to test pathfinding between two points. For physics constraints between objects (hinges, springs), use 'physics' with the 'create_joint' action instead.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "create_agent", "create_area", "create_link", "generate", "get_status", "query_path" } },
                ["id"] = new { type = "string", description = "Target GameObject GUID. Required for: create_agent." },
                ["position"] = new { type = "string", description = "Position as 'x,y,z'. Used by: create_area." },
                ["size"] = new { type = "string", description = "Area size as 'x,y,z'. Used by: create_area." },
                ["start_position"] = new { type = "string", description = "Link start as 'x,y,z'. Required for: create_link." },
                ["end_position"] = new { type = "string", description = "Link end as 'x,y,z'. Required for: create_link." },
                ["from"] = new { type = "string", description = "Path start as 'x,y,z'. Required for: query_path." },
                ["to"] = new { type = "string", description = "Path end as 'x,y,z'. Required for: query_path." },
                ["speed"] = new { type = "number", description = "Agent speed. Used by: create_agent." },
                ["radius"] = new { type = "number", description = "Agent radius. Used by: create_agent." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 16: session ─────────────────────────────────────────────

    internal static readonly object Session = new
    {
        name = "session",
        description = "Manage editor sessions: list all open editing sessions, switch between them, and load new scenes. Use this when working with multiple scenes simultaneously or when you need to switch editing context. For scene content operations within the active session, use 'scene' and 'gameobject'. For play mode and editor state, use 'editor'.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "list", "set_active", "load_scene" } },
                ["session_id"] = new { type = "string", description = "Session identifier. Required for: set_active." },
                ["path"] = new { type = "string", description = "Scene file path. Required for: load_scene." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 17: project ─────────────────────────────────────────────

    internal static readonly object Project = new
    {
        name = "project",
        description = "Read and modify project-level settings: collision layer configuration, input action bindings, and project metadata. Use 'set_collision_rule' to control which physics layers collide with each other. Use 'get_input' to see all configured input actions and their bindings. For runtime project info, use the 'get_info' action.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "get_collision", "set_collision_rule", "get_input", "get_info" } },
                ["layer_a"] = new { type = "string", description = "First collision layer name. Required for: set_collision_rule." },
                ["layer_b"] = new { type = "string", description = "Second collision layer name. Required for: set_collision_rule." },
                ["collides"] = new { type = "boolean", description = "Whether the layers should collide. Required for: set_collision_rule." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 18: cloud ───────────────────────────────────────────────

    internal static readonly object Cloud = new
    {
        name = "cloud",
        description = "Search the s&box cloud asset store, get package details, list versions, and mount packages into the current project. Returns package idents, titles, types, and thumbnail URLs. Use 'get_package' to get the 'primaryAsset' field — this contains the local vmdl path needed for Model.Load() and scene file references. Use 'get_versions' to list all available revisions of a package (useful for pinning map versions). Use 'mount' to download and register a package in the project's .sbproj file — optionally pin to a specific revision. Runs on background thread — does not block the editor. For local project assets, use 'asset_query' instead.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "search", "get_package", "get_versions", "mount" } },
                ["query"] = new { type = "string", description = "Search term. Required for: search." },
                ["ident"] = new { type = "string", description = "Package identifier in 'org.name' format. Required for: get_package, get_versions, mount." },
                ["type"] = new { type = "string", description = "Filter by asset type. Used by: search." },
                ["max_results"] = new { type = "integer", description = "Max search results (1-50). Used by: search. Default: 10." },
                ["revision"] = new { type = "integer", description = "Pin to a specific package revision number (from get_versions). Used by: mount." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };

    // ── Tool 19: terrain ─────────────────────────────────────────────

    internal static readonly object Terrain = new
    {
        name = "terrain",
        description = "Full terrain editing: create, sculpt heightmaps, apply procedural noise, paint materials, punch holes, import/export heightmaps. Use 'set_height' to sculpt with modes: set, raise, lower, flatten, smooth. Use 'noise' for procedural generation (perlin, ridged, billow). Use 'get_height' / 'get_material_at' to query terrain state. Use 'set_hole' for caves/tunnels. Use 'import_heightmap' / 'export_heightmap' for grayscale PNG I/O. Always call 'sync' after edits.",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", description = "The operation to perform.", @enum = new[] { "create", "configure", "get_info", "get_height", "get_height_region", "set_height", "noise", "erode", "stamp", "add_material", "remove_material", "get_material_at", "blend_materials", "set_hole", "paint_material", "import_heightmap", "export_heightmap", "sync" } },
                ["id"] = new { type = "string", description = "Terrain GameObject GUID. Optional for most actions (auto-finds if only one terrain exists)." },
                ["name"] = new { type = "string", description = "GameObject name for the terrain. IMPORTANT: determines the .terrain storage filename (terrain_data/{name}.terrain). Use unique names per scene to avoid overwriting other terrains. Used by: create. Default: scene name." },
                ["size"] = new { type = "number", description = "Total terrain size in inches (= World Scale × Heightmap Size). e.g. 19968 for ~500m with scale=39, res=512. Used by: create. Default: 1024." },
                ["height"] = new { type = "number", description = "Height value in inches. create: max terrain elevation (e.g. 10000 ~= 254m). set_height: target or delta height in world units." },
                ["resolution"] = new { type = "integer", description = "Heightmap resolution (512, 1024, 2048, 4096, or 8192). Higher = more detail but more VRAM: 512=~1.5MB, 2048=24MB, 4096=96MB. Used by: create. Default: 512." },
                ["position"] = new { type = "string", description = "s&box world position 'x,y,z'. Terrain occupies [terrainPos, terrainPos+size] in X and Y. Use get_info to find terrain position and size. Required for: get_height, set_height, get_material_at, set_hole, paint_material, stamp, get_height_region, blend_materials." },
                ["material"] = new { type = "string", description = "Terrain material name. Required for: paint_material." },
                ["material_path"] = new { type = "string", description = ".terrain_material asset path. Used by: add_material, remove_material." },
                ["material_index"] = new { type = "integer", description = "Material index. Used by: remove_material." },
                ["radius"] = new { type = "number", description = "Brush radius (world units). Used by: set_height, set_hole, paint_material. Default: 50." },
                ["strength"] = new { type = "number", description = "Brush strength 0-1. Used by: set_height, paint_material. Default: 1." },
                ["mode"] = new { type = "string", description = "set_height: 'set'|'raise'|'lower'|'flatten'|'smooth'. noise: 'set'|'add'|'multiply'. Default: 'set'." },
                ["falloff"] = new { type = "string", description = "Brush falloff: 'linear'|'smooth'|'none'. Used by: set_height. Default: 'linear'." },
                ["type"] = new { type = "string", description = "Noise type: 'perlin'|'ridged'|'billow'. Used by: noise. Default: 'perlin'." },
                ["scale"] = new { type = "number", description = "Noise frequency scale. Used by: noise. Default: 0.01." },
                ["amplitude"] = new { type = "number", description = "Noise height amplitude (world units). Used by: noise. Default: 30% of terrain height." },
                ["octaves"] = new { type = "integer", description = "Noise octave layers. Used by: noise. Default: 4." },
                ["persistence"] = new { type = "number", description = "Amplitude decay per octave. Used by: noise. Default: 0.5." },
                ["lacunarity"] = new { type = "number", description = "Frequency increase per octave. Used by: noise. Default: 2.0." },
                ["seed"] = new { type = "integer", description = "Noise random seed. Used by: noise. Default: 42." },
                ["offset_x"] = new { type = "number", description = "Noise X offset. Used by: noise." },
                ["offset_y"] = new { type = "number", description = "Noise Y offset. Used by: noise." },
                ["enabled"] = new { type = "boolean", description = "true = punch hole, false = fill. Used by: set_hole. Default: true." },
                ["path"] = new { type = "string", description = "File path. import_heightmap: source image. export_heightmap: output PNG." },
                ["iterations"] = new { type = "integer", description = "Number of erosion droplets. Used by: erode. Default: 50000." },
                ["erosion_rate"] = new { type = "number", description = "How fast droplets erode. Used by: erode. Default: 0.3." },
                ["deposition_rate"] = new { type = "number", description = "How fast sediment deposits. Used by: erode. Default: 0.3." },
                ["evaporation_rate"] = new { type = "number", description = "Water evaporation rate. Used by: erode. Default: 0.01." },
                ["gravity"] = new { type = "number", description = "Gravity affecting flow speed. Used by: erode. Default: 4." },
                ["inertia"] = new { type = "number", description = "Droplet direction inertia 0-1. Used by: erode. Default: 0.05." },
                ["capacity"] = new { type = "number", description = "Sediment capacity factor. Used by: erode. Default: 4." },
                ["lifetime"] = new { type = "integer", description = "Max droplet lifetime in steps. Used by: erode. Default: 30." },
                ["erosion_radius"] = new { type = "integer", description = "Erosion brush radius in texels. Used by: erode. Default: 3." },
                ["angle"] = new { type = "number", description = "Rotation angle in degrees. Used by: stamp (for ridge/valley). Default: 0." },
                ["region_size"] = new { type = "number", description = "Region size in world units. Used by: get_height_region. Default: 100." },
                ["samples"] = new { type = "integer", description = "Samples per axis. Used by: get_height_region. Default: 10." },
                ["base_index"] = new { type = "integer", description = "Base material index 0-31. Used by: blend_materials." },
                ["overlay_index"] = new { type = "integer", description = "Overlay material index 0-31. Used by: blend_materials." },
                ["blend"] = new { type = "number", description = "Blend factor 0-1 between base and overlay. Used by: blend_materials. Default: 0.5." },
                ["lod_levels"] = new { type = "integer", description = "LOD level count. Used by: configure." },
                ["subdivision"] = new { type = "integer", description = "Subdivision factor. Used by: configure." }
            },
            required = new[] { "action" },
            additionalProperties = false
        }
    };
}
