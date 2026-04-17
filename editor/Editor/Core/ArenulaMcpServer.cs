using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Editor;
using Sandbox;

namespace Arenula;

/// <summary>
/// HTTP/SSE transport layer for the MCP server.
/// Responsible for accepting connections, managing SSE sessions, and routing
/// raw HTTP requests. Tool logic lives in handler classes,
/// dispatch in RpcDispatcher, and schemas in tool definition classes.
/// </summary>
public static class ArenulaMcpServer
{
    [ConVar( "mcp_server_port", ConVarFlags.Saved )]
    public static int Port { get; set; } = 8098;

    // ── GUI events & state ─────────────────────────────────────────────────
    public static event Action OnServerStateChanged;
    public static event Action<string> OnLogMessage;
    public static bool IsRunning   => _listener != null && _listener.IsListening;
    public static int  SessionCount => _sessions.Count;

    /// <summary>Safely dispatches an action to the main thread, swallowing exceptions if the thread is gone (e.g. during cancel).</summary>
    private static async void SafeRunOnMainThread( Action action )
    {
        try
        {
            await GameTask.RunInThreadAsync( action );
        }
        catch { /* Main thread unavailable (cancelled / disposed) – safe to ignore */ }
    }

    private static void LogInfo( string msg )
    {
        SafeRunOnMainThread( () =>
        {
            Log.Info( msg );
            OnLogMessage?.Invoke( msg );
        } );
    }

    private static void LogError( string msg )
    {
        SafeRunOnMainThread( () =>
        {
            Log.Error( msg );
            OnLogMessage?.Invoke( $"[ERROR] {msg}" );
        } );
    }

    private static void LogWarning( string msg )
    {
        SafeRunOnMainThread( () =>
        {
            Log.Warning( msg );
            OnLogMessage?.Invoke( $"[WARNING] {msg}" );
        } );
    }

    private static void NotifyStateChanged()
    {
        SafeRunOnMainThread( () => OnServerStateChanged?.Invoke() );
    }

    // ── Internal state ─────────────────────────────────────────────────────
    private static HttpListener _listener;
    private static CancellationTokenSource _cts;
    private static readonly ConcurrentDictionary<string, McpSession> _sessions = new();

    // Tracks in-flight RPC tasks so StopServer can wait for them to finish
    private static readonly ConcurrentDictionary<Guid, Task> _inflightTasks = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented          = false
    };

    // ── Global log capture ────────────────────────────────────────────────
    private static bool _loggerHooked;

    private static void OnGlobalLog( LogEvent e )
    {
        var prefix = e.Level switch
        {
            LogLevel.Error   => "[ERROR] ",
            LogLevel.Warn    => "[WARNING] ",
            LogLevel.Trace   => "[TRACE] ",
            _                => ""
        };
        var logger = string.IsNullOrEmpty( e.Logger ) ? "" : $"{e.Logger} ";
        EditorHandler.AppendLog( $"{e.Time:HH:mm:ss} {logger}{prefix}{e.Message}" );
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    /// <summary>
    /// On hotload, stop the existing server (which retains its listener reference via
    /// preserved static state) and restart it so new handler code is picked up.
    /// </summary>
    [EditorEvent.Hotload]
    public static void OnHotload()
    {
        if ( IsRunning ) StopServer();
        StartServer();
    }

    public static void StartServer()
    {
        if ( _listener != null && _listener.IsListening )
        {
            LogInfo( "MCP Server is already running" );
            return;
        }

        try
        {
            // Hook into the global editor log stream
            if ( !_loggerHooked )
            {
                EditorUtility.AddLogger( OnGlobalLog );
                _loggerHooked = true;
            }

            _listener = new HttpListener();
            _listener.Prefixes.Add( $"http://localhost:{Port}/" );
            _listener.Prefixes.Add( $"http://127.0.0.1:{Port}/" );
            _listener.Start();

            _cts = new CancellationTokenSource();
            Task.Run( () => ListenLoop( _cts.Token ) );

            LogInfo( $"Started Model Context Protocol Server on port {Port}" );
            NotifyStateChanged();
        }
        catch ( Exception ex )
        {
            LogError( $"Failed to start MCP Server: {ex.Message}" );
        }
    }

    public static void StopServer()
    {
        try
        {
            // Unhook the global logger
            if ( _loggerHooked )
            {
                EditorUtility.RemoveLogger( OnGlobalLog );
                _loggerHooked = false;
            }

            _cts?.Cancel();

            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;

            // Don't wait for in-flight tasks — they may be blocked waiting for the
            // main thread (GameTask.RunInThreadAsync) which would deadlock here.
            // Just clear the tracking dictionary; the tasks will see the cancelled token
            // and wind down on their own.
            _inflightTasks.Clear();

            foreach ( var session in _sessions.Values )
            {
                try { session.Tcs.TrySetResult( true ); } catch { }
                try { session.SseResponse?.Close(); } catch { }
            }
            _sessions.Clear();

            LogInfo( "Stopped Model Context Protocol Server" );
            NotifyStateChanged();
        }
        catch ( Exception ex )
        {
            try { LogError( $"Error stopping MCP Server: {ex.Message}" ); } catch { }
        }
    }

    // ── HTTP listen loop ───────────────────────────────────────────────────

    private static async Task ListenLoop( CancellationToken token )
    {
        while ( !token.IsCancellationRequested && _listener != null && _listener.IsListening )
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run( () => HandleContext( context ), token );
            }
            catch ( Exception ex ) when ( ex is not ObjectDisposedException )
            {
                LogError( $"Error in MCP listen loop: {ex.Message}" );
            }
        }
    }

    private static async Task HandleContext( HttpListenerContext context )
    {
        // Top-level guard: this is invoked via `_ = Task.Run(...)` in ListenLoop, so any
        // exception that escapes is unobserved and surfaces on the finalizer thread as
        // "A Task's exception(s) were not observed". The most common path is
        // ObjectDisposedException on res when the listener is torn down mid-request.
        try
        {
            var req = context.Request;
            var res = context.Response;

            res.Headers.Add( "Access-Control-Allow-Origin",  "*" );
            res.Headers.Add( "Access-Control-Allow-Methods", "GET, POST, OPTIONS" );
            res.Headers.Add( "Access-Control-Allow-Headers", "*" );

            if ( req.HttpMethod == "OPTIONS" ) { res.StatusCode = 200; res.Close(); return; }

            try
            {
                if      ( req.Url.AbsolutePath == "/sse"     && req.HttpMethod == "GET"  ) await HandleSse( req, res );
                else if ( req.Url.AbsolutePath == "/message" && req.HttpMethod == "POST" ) await HandleMessage( req, res );
                else    { res.StatusCode = 404; res.Close(); }
            }
            catch ( ObjectDisposedException )
            {
                // Listener/response torn down mid-request. Nothing to clean up safely.
            }
            catch ( Exception ex )
            {
                try { LogError( $"Error handling MCP request: {ex.Message}" ); } catch { }
                // Response cleanup must itself be guarded — if the original exception
                // came from a disposed response, these lines will re-throw.
                try { res.StatusCode = 500; res.Close(); } catch { }
            }
        }
        catch ( Exception ex )
        {
            try { LogError( $"Unhandled error in MCP HandleContext: {ex.Message}" ); } catch { }
        }
    }

    // ── SSE connection ─────────────────────────────────────────────────────

    private static async Task HandleSse( HttpListenerRequest req, HttpListenerResponse res )
    {
        var sessionId = Guid.NewGuid().ToString();
        var session   = new McpSession { SessionId = sessionId, SseResponse = res };
        _sessions[sessionId] = session;

        res.ContentType = "text/event-stream";
        res.Headers.Add( "Cache-Control", "no-cache" );
        res.Headers.Add( "Connection",    "keep-alive" );

        try
        {
            var msg    = $"event: endpoint\ndata: /message?sessionId={sessionId}\n\n";
            var buffer = Encoding.UTF8.GetBytes( msg );
            await res.OutputStream.WriteAsync( buffer, 0, buffer.Length );
            await res.OutputStream.FlushAsync();

            LogInfo( $"Created new MCP SSE session: {sessionId}" );
            NotifyStateChanged();

            // Ping every 30 s so we detect when the client disconnects
            while ( !_cts.IsCancellationRequested )
            {
                var done = await Task.WhenAny( session.Tcs.Task, Task.Delay( 30_000 ) );
                if ( done == session.Tcs.Task || _cts.IsCancellationRequested ) break;
                var ping = Encoding.UTF8.GetBytes( ": ping\n\n" );
                await res.OutputStream.WriteAsync( ping, 0, ping.Length );
                await res.OutputStream.FlushAsync(); // throws if client is gone → caught below
            }
        }
        catch ( Exception ex ) { LogError( $"SSE connection error: {ex.Message}" ); }
        finally
        {
            _sessions.TryRemove( sessionId, out _ );
            try { res.Close(); } catch { }
            LogInfo( $"Closed MCP SSE session: {sessionId}" );
            NotifyStateChanged();
        }
    }

    // ── Message (JSON-RPC) ─────────────────────────────────────────────────

    private static async Task HandleMessage( HttpListenerRequest req, HttpListenerResponse res )
    {
        var sessionId = req.QueryString["sessionId"];
        if ( string.IsNullOrEmpty( sessionId ) || !_sessions.TryGetValue( sessionId, out var session ) )
        {
            res.StatusCode = 400; res.Close(); return;
        }

        using var reader = new StreamReader( req.InputStream, Encoding.UTF8 );
        var body = await reader.ReadToEndAsync();

        try
        {
            using var doc = JsonDocument.Parse( body );
            var root      = doc.RootElement;
            string method = root.TryGetProperty( "method", out var m ) ? m.GetString() : null;
            object id     = null;

            if ( root.TryGetProperty( "id", out var idProp ) )
            {
                if      ( idProp.ValueKind == JsonValueKind.Number ) id = idProp.GetInt32();
                else if ( idProp.ValueKind == JsonValueKind.String ) id = idProp.GetString();
            }

            res.StatusCode = 202;
            res.Close();

            if ( id != null )
            {
                var bodyCopy   = body;
                var idCopy     = id;
                var methodCopy = method;
                var taskId     = Guid.NewGuid();
                var task       = GameTask.RunInThreadAsync( async () =>
                {
                    try
                    {
                        await RpcDispatcher.ProcessRpcRequest(
                            session, idCopy, methodCopy, bodyCopy,
                            JsonOptions, LogInfo, LogError );
                    }
                    catch ( Exception ex )
                    {
                        LogError( $"ProcessRpcRequest unhandled fault: {ex.Message}" );
                        var errResponse = new
                        {
                            jsonrpc = "2.0",
                            id      = idCopy,
                            result  = (object)null,
                            error   = new { code = -32603, message = $"Internal error: {ex.Message}" }
                        };
                        var errJson = JsonSerializer.Serialize( errResponse, JsonOptions );
                        await SendSseEvent( session, "message", errJson );
                    }
                    finally
                    {
                        _inflightTasks.TryRemove( taskId, out _ );
                    }
                } );
                _inflightTasks[taskId] = task;
            }
            else if ( method == "notifications/initialized" )
            {
                session.Initialized = true;
                LogInfo( $"MCP Session {sessionId} initialized." );
                NotifyStateChanged();
            }
        }
        catch ( Exception ex )
        {
            LogError( $"Error parsing JSON-RPC: {ex.Message}" );
        }
    }

    // ── SSE write ──────────────────────────────────────────────────────────

    /// <summary>Writes a single SSE event to the given session's output stream.</summary>
    internal static async Task SendSseEvent( McpSession session, string eventName, string data )
    {
        if ( session.SseResponse == null || !session.SseResponse.OutputStream.CanWrite ) return;
        try
        {
            var msg    = $"event: {eventName}\ndata: {data}\n\n";
            var buffer = Encoding.UTF8.GetBytes( msg );
            await session.SseResponse.OutputStream.WriteAsync( buffer, 0, buffer.Length );
            await session.SseResponse.OutputStream.FlushAsync();
        }
        catch ( Exception ex )
        {
            LogWarning( $"Failed to send SSE event to session {session.SessionId}: {ex.Message}" );
        }
    }
}
