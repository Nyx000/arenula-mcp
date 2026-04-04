using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// Handles compilation-related actions: trigger, status, errors, generate_solution, wait.
/// Async handler — dispatched on background thread, switches to main thread for editor API calls.
/// </summary>
internal static class CompileHandler
{
    /// <summary>
    /// Cache of the most recent compile outputs, populated by <see cref="Trigger"/>.
    /// Used by <see cref="GetStatus"/> and <see cref="GetErrors"/> to read compiler state
    /// without triggering a new build.
    /// </summary>
    private static CompilerOutput[] _lastOutputs;

    internal static async Task<object> HandleAsync( string action, JsonElement args )
    {
        try
        {
            return action switch
            {
                "trigger"           => await Trigger(),
                "status"            => await GetStatus(),
                "errors"            => await GetErrors( args ),
                "generate_solution" => await GenerateSolution(),
                "wait"              => await Wait( args ),
                _                   => HandlerBase.Error( $"Unknown action '{action}' for tool 'compile'.", action,
                                        "Valid actions: trigger, status, errors, generate_solution, wait" )
            };
        }
        catch ( Exception ex )
        {
            return HandlerBase.Error( ex.Message, action, "Check that the editor is running and a project is loaded." );
        }
    }

    // ── trigger ───────────────────────────────────────────────────────────

    /// <summary>
    /// Trigger compilation of all active projects.
    /// Uses EditorUtility.Projects.Compile() which returns CompilerOutput[] per project.
    /// </summary>
    private static async Task<object> Trigger()
    {
        await GameTask.MainThread();

        var projects = EditorUtility.Projects.GetAll();
        if ( projects == null || projects.Count == 0 )
            return HandlerBase.Error( "No projects found.", "trigger", "Open a project in the s&box editor." );

        // Filter to active, non-transient projects that have code
        var compilable = projects
            .Where( p => p.Active && !p.IsTransient && !p.IsBuiltIn && p.HasCodePath() )
            .ToList();

        if ( compilable.Count == 0 )
            return HandlerBase.Error( "No compilable projects found.", "trigger",
                "Ensure the project is active and has a Code folder." );

        var allOutputs = new List<CompilerOutput>();
        var results = new List<object>();

        foreach ( var project in compilable )
        {
            var outputs = await EditorUtility.Projects.Compile( project, null );
            if ( outputs != null )
            {
                allOutputs.AddRange( outputs );
                foreach ( var output in outputs )
                {
                    results.Add( new
                    {
                        compiler = output.Compiler?.Name ?? "unknown",
                        successful = output.Successful,
                        diagnosticCount = output.Diagnostics?.Count ?? 0
                    } );
                }
            }
        }

        _lastOutputs = allOutputs.ToArray();

        return HandlerBase.Success( new
        {
            message = "Compilation triggered.",
            projectCount = compilable.Count,
            compilerResults = results
        } );
    }

    // ── status ────────────────────────────────────────────────────────────

    /// <summary>
    /// Report compilation status for all known compilers.
    /// Uses cached outputs from last Trigger, plus checks live compiler state.
    /// </summary>
    private static async Task<object> GetStatus()
    {
        await GameTask.MainThread();

        var projects = EditorUtility.Projects.GetAll();
        if ( projects == null || projects.Count == 0 )
            return HandlerBase.Error( "No projects found.", "status", "Open a project in the s&box editor." );

        var projectStatuses = new List<object>();

        foreach ( var project in projects.Where( p => p.Active && !p.IsTransient && !p.IsBuiltIn ) )
        {
            projectStatuses.Add( new
            {
                name = project.Config?.Title ?? project.Package?.Title ?? "unknown",
                ident = project.Config?.FullIdent,
                hasCompiler = project.HasCompiler,
                hasCode = project.HasCodePath(),
                active = project.Active
            } );
        }

        // If we have cached compiler outputs, report their state
        var compilerStatuses = new List<object>();

        if ( _lastOutputs != null )
        {
            foreach ( var output in _lastOutputs )
            {
                var compiler = output.Compiler;
                if ( compiler == null ) continue;

                compilerStatuses.Add( new
                {
                    name = compiler.Name,
                    isBuilding = compiler.IsBuilding,
                    needsBuild = compiler.NeedsBuild,
                    buildSuccess = compiler.BuildSuccess,
                    diagnosticCount = compiler.Diagnostics?.Length ?? 0,
                    groupName = compiler.Group?.Name,
                    groupIsBuilding = compiler.Group?.IsBuilding ?? false,
                    groupNeedsBuild = compiler.Group?.NeedsBuild ?? false
                } );
            }
        }

        return HandlerBase.Success( new
        {
            projects = projectStatuses,
            compilers = compilerStatuses,
            hasCompilerCache = _lastOutputs != null,
            hint = compilerStatuses.Count == 0
                ? "Run compile.trigger first to populate compiler state."
                : null
        } );
    }

    // ── errors ────────────────────────────────────────────────────────────

    /// <summary>
    /// Return compiler diagnostics (errors/warnings) from the last build.
    /// Supports optional severity filter: "error", "warning", "info", "hidden".
    /// Microsoft.CodeAnalysis.Diagnostic is available because s&box ships Roslyn for its compiler.
    /// </summary>
    private static async Task<object> GetErrors( JsonElement args )
    {
        await GameTask.MainThread();

        var severityFilter = HandlerBase.GetString( args, "severity" );

        if ( _lastOutputs == null || _lastOutputs.Length == 0 )
        {
            return HandlerBase.Error(
                "No compile outputs available. Run compile.trigger first.",
                "errors",
                "Use compile.trigger to build, then compile.errors to see diagnostics." );
        }

        var diagnostics = new List<DiagnosticEntry>();

        foreach ( var output in _lastOutputs )
        {
            var compiler = output.Compiler;
            var compilerDiagnostics = compiler?.Diagnostics;

            if ( compilerDiagnostics == null || compilerDiagnostics.Length == 0 )
                continue;

            foreach ( var diag in compilerDiagnostics )
            {
                var severity = diag.Severity.ToString().ToLowerInvariant();

                // Apply severity filter if specified
                if ( !string.IsNullOrEmpty( severityFilter ) &&
                     !string.Equals( severity, severityFilter, StringComparison.OrdinalIgnoreCase ) )
                    continue;

                string filePath = null;
                int? line = null;
                int? column = null;

                // Location and GetMappedLineSpan() are Roslyn APIs on Microsoft.CodeAnalysis.Diagnostic
                try
                {
                    var lineSpan = diag.Location?.GetMappedLineSpan();
                    if ( lineSpan.HasValue && lineSpan.Value.IsValid )
                    {
                        filePath = lineSpan.Value.Path;
                        line = lineSpan.Value.StartLinePosition.Line + 1;
                        column = lineSpan.Value.StartLinePosition.Character + 1;
                    }
                }
                catch
                {
                    // Location APIs may not be available in all sandbox contexts
                }

                diagnostics.Add( new DiagnosticEntry
                {
                    id = diag.Id,
                    severity = severity,
                    message = diag.GetMessage(),
                    compiler = compiler.Name,
                    file = filePath,
                    line = line,
                    column = column,
                    sortOrder = severity switch
                    {
                        "error" => 0,
                        "warning" => 1,
                        "info" => 2,
                        _ => 3
                    }
                } );
            }
        }

        var sorted = diagnostics.OrderBy( d => d.sortOrder ).ToList();

        return HandlerBase.Success( new
        {
            totalCount = sorted.Count,
            filter = severityFilter,
            diagnostics = sorted.Select( d => new
            {
                d.id,
                d.severity,
                d.message,
                d.compiler,
                d.file,
                d.line,
                d.column
            } )
        } );
    }

    /// <summary>Internal record for sorting diagnostics before serialization.</summary>
    private sealed class DiagnosticEntry
    {
        public string id;
        public string severity;
        public string message;
        public string compiler;
        public string file;
        public int? line;
        public int? column;
        public int sortOrder;
    }

    // ── generate_solution ─────────────────────────────────────────────────

    /// <summary>
    /// Regenerate the .sln file for all projects.
    /// </summary>
    private static async Task<object> GenerateSolution()
    {
        await GameTask.MainThread();
        await EditorUtility.Projects.GenerateSolution();
        return HandlerBase.Confirm( "Solution file regenerated successfully." );
    }

    // ── wait ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Block until all compilations complete, with configurable timeout.
    /// EditorUtility.Projects.WaitForCompiles() is a static Task that resolves when
    /// all local project compiles finish.
    /// </summary>
    private static async Task<object> Wait( JsonElement args )
    {
        var timeoutMs = HandlerBase.GetInt( args, "timeout_ms", 60000 );

        if ( timeoutMs < 1000 )
            timeoutMs = 1000;
        if ( timeoutMs > 300000 )
            timeoutMs = 300000;

        try
        {
            // WaitForCompiles is a static editor API — kick it off from main thread
            await GameTask.MainThread();
            var waitTask = EditorUtility.Projects.WaitForCompiles();

            // Race against timeout on background thread
            var completed = await Task.WhenAny( waitTask, Task.Delay( timeoutMs ) );

            if ( completed != waitTask )
            {
                return HandlerBase.Error(
                    $"Compilation did not complete within {timeoutMs}ms.",
                    "wait",
                    "Increase timeout_ms or check for compile errors." );
            }

            // Await the actual task to propagate any exceptions
            await waitTask;

            // Read final state on main thread
            await GameTask.MainThread();

            if ( _lastOutputs != null )
            {
                var anyFailed = _lastOutputs.Any( o => o.Compiler != null && !o.Compiler.BuildSuccess );
                var totalDiagnostics = _lastOutputs
                    .Where( o => o.Compiler?.Diagnostics != null )
                    .Sum( o => o.Compiler.Diagnostics.Length );

                return HandlerBase.Success( new
                {
                    message = "Compilation finished.",
                    allSucceeded = !anyFailed,
                    totalDiagnostics,
                    timeoutMs
                } );
            }

            return HandlerBase.Confirm( "Compilation finished." );
        }
        catch ( OperationCanceledException )
        {
            return HandlerBase.Error(
                $"Compilation wait cancelled after {timeoutMs}ms.",
                "wait",
                "Increase timeout_ms or check for compile errors." );
        }
    }
}
