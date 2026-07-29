namespace StationeersHTTPInstructions;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json.Linq;

using Assets.Scripts.Objects.Electrical;

public class Server
{
    public int Port;
    public HttpListener Listener;
    public Dictionary<string, string> Data;
    public HashSet<int> UsingChips = []; // todo: keep track of chips using this server, and close it when no chips are using it anymore
    protected readonly object _Lock = new();

    public Server(int port)
    {
        Port = port;
        Listener = new HttpListener();
        Data = [];
        Listener.Prefixes.Add($"http://*:{port}/");
        try
        {
            Listener.Start();
            _ = ListenLoop();
            L.Debug($"Started HTTP listener on port {port}");
        }
        catch (Exception ex)
        {
            L.Error($"Failed to start HTTP listener on port {port}: {ex}");
            Listener.Close();
            throw new ProgrammableChipException(
                ProgrammableChipException.ICExceptionType.Unknown, 0);
        }
    }

    public void Close()
    {
        Listener.Stop();
        Listener.Close();
    }

    protected async Task ListenLoop()
    {
        while (Listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await Listener.GetContextAsync();
            }
            catch (Exception ex)
            {
                L.Error($"HTTP listener exception: {ex}");
                break;
            }

            _ = HandleRequest(context);
        }
    }

    protected async Task HandleRequest(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                string body = await reader.ReadToEndAsync();

                lock (_Lock)
                {
                    Data[context.Request.Url.AbsolutePath] = body;
                }

                L.Debug($"Received POST '{body}', parsed value {body}");
            }

            else if (context.Request.HttpMethod == "GET")
            {
                string path = context.Request.Url.AbsolutePath;
                string value = GetValue(path, true);

                if (value != null)
                {
                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(value);
                    context.Response.ContentLength64 = buffer.Length;
                    await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    L.Debug($"Responded to GET '{path}' with value '{value}'");
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
            }

            context.Response.StatusCode = 200;
        }
        catch (Exception ex)
        {
            L.Debug($"HTTP POST handler exception: {ex}");
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.Close();
        }
    }

    public void SetValue(string key, string value)
    {
        lock (_Lock)
        {
            Data[key] = value;
        }
    }

    public string GetValue(string key, bool removeAfterGet = false)
    {
        lock (_Lock)
        {
            if (Data.TryGetValue(key, out string value))
            {
                if (removeAfterGet)
                    Data.Remove(key);
                return value;
            }
            else
            {
                return null;
            }
        }
    }
}

public abstract class BaseHTTPListenOperation : BaseHTTPOperation
{
    protected static Dictionary<int, Server> _Servers = [];

    protected Server _Server;
    protected string _Path;

    public BaseHTTPListenOperation(ProgrammableChip chip, int lineNumber)
        : base(chip, lineNumber)
    { }

    protected void InitServer(int port)
    {
        if (!_Servers.ContainsKey(port))
            _Servers[port] = new Server(port);
        _Server = _Servers[port];
    }

    public static void Cleanup()
    {
        foreach (var server in _Servers.Values)
            server.Close();
        _Servers.Clear();
    }
}

public class HTTPOnGetOperation : BaseHTTPListenOperation
{
    public static string OP_NAME = "http_on_get";
    public HTTPOnGetOperation(ProgrammableChip chip, int lineNumber, List<string> tokens)
        : base(chip, lineNumber)
    {
        L.Debug($"Creating {OP_NAME} operation with tokens: {string.Join(", ", tokens)}");

        if (tokens.Count < 4)
            throw new ProgrammableChipException(
                ProgrammableChipException.ICExceptionType.IncorrectArgumentCount,
                lineNumber);

        InitServer(int.Parse(tokens[1]));
        _Path = tokens[2];
        ParseInputs(tokens[3]);
    }

    public override int Execute(int index)
    {
        try
        {
            _Server.SetValue(_Path, _InputTemplate.GetString());
        }
        catch (Exception ex)
        {
            L.Error($"HTTP GET operation failed: {ex}");
            SetOutputs(false, null);
        }

        return index + 1;
    }
}

public class HTTPOnPostOperation : BaseHTTPListenOperation
{
    public static string OP_NAME = "http_on_post";

    public HTTPOnPostOperation(ProgrammableChip chip, int lineNumber, List<string> tokens)
        : base(chip, lineNumber)
    {
        L.Debug($"Creating {OP_NAME} operation with tokens: {string.Join(", ", tokens)}");

        if (tokens.Count < 4)
            throw new ProgrammableChipException(
                ProgrammableChipException.ICExceptionType.IncorrectArgumentCount,
                lineNumber);

        InitServer(int.Parse(tokens[1]));
        _Path = tokens[2];
        ParseOutputs(tokens[3]);
    }

    public override int Execute(int index)
    {
        try
        {
            var value = _Server.GetValue(_Path, true);
            L.Debug($"HTTP On POST operation retrieved value: {value!=null}, {value}");
            SetOutputs(value != null, value);
        }
        catch (Exception ex)
        {
            L.Error($"HTTP On POST operation failed: {ex}");
            SetOutputs(false, null);
        }
        return index + 1;
    }
}