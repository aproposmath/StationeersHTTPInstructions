namespace StationeersHTTPInstructions;

using System.Text.RegularExpressions;
using System.Text;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using Newtonsoft.Json.Linq;

using Assets.Scripts.Objects.Electrical;

public class TemplateString
{
    private readonly string _template;
    private readonly HashSet<ProgrammableChip._Operation.DoubleValueVariable> _variables = [];

    public TemplateString(string token, ProgrammableChip chip, int lineNumber)
    {
        _template = token.Trim('\'');

        HashSet<string> names = [];

        foreach (Match match in Regex.Matches(_template, @"\$\{([A-Za-z_][A-Za-z0-9_.]*)\}"))
            if (!names.Contains(match.Groups[1].Value))
                names.Add(match.Groups[1].Value);

        foreach (var name in names)
            _variables.Add(new(chip, lineNumber, name, InstructionInclude.MaskDoubleValue, throwException: false));
    }

    public string GetString()
    {
        string result = _template;
        foreach (var variable in _variables)
        {
            double value = variable.GetVariableValue(ProgrammableChip._AliasTarget.Register);
            result = result.Replace($"${{{variable._Alias}}}", value.ToString());
        }
        return result;
    }
}

public abstract class BaseHTTPOperation(ProgrammableChip chip, int lineNumber) : ProgrammableChip._Operation(chip, lineNumber)
{
    protected static HttpClient _Client = null;
    protected TemplateString UrlTemplate = null;
    protected TemplateString _OutputTemplate = null;
    protected TemplateString _InputTemplate = null;
    protected Dictionary<string, IndexVariable> _OutputVariables = null;
    protected IndexVariable _SuccessOutput = null;

    protected static HttpClient Client
    {
        get
        {
            if (_Client == null)
            {
                _Client = new HttpClient();
                _Client.DefaultRequestHeaders.Add("User-Agent", "StationeersHTTPInstructions/1.0");
            }

            return _Client;
        }
    }

    public static readonly Dictionary<string, Type> OperationTypes = new()
    {
        { "http_get", typeof(HTTPGetOperation) },
        { "http_post", typeof(HTTPPostOperation) },
        { "http_on_post", typeof(HTTPOnPostOperation) },
        { "http_on_get", typeof(HTTPOnGetOperation) }
    };

    public DoubleValueVariable MakeInputVariable(string registerOrValueCode) => new(_Chip, _LineNumber, registerOrValueCode, InstructionInclude.MaskDoubleValue, throwException: false);
    public IndexVariable MakeOutputVariable(string registerCode) => new(_Chip, _LineNumber, registerCode, InstructionInclude.MaskStoreIndex, throwException: false);

    public double GetJsonValue(JToken root, string jsonPath)
    {
        try
        {
            if (root == null) return double.NaN;

            var match = Regex.Match(jsonPath, @"^(.*)\[(\d*):(\d*)\]$");
            var basePath = jsonPath;
            var haveRange = false;
            var start = 0;
            var end = 0;

            if (match.Success)
            {
                basePath = match.Groups[1].Value;
                var startStr = match.Groups[2].Value;
                var endStr = match.Groups[3].Value;
                if (!string.IsNullOrEmpty(startStr))
                    start = int.Parse(match.Groups[2].Value);
                if (!string.IsNullOrEmpty(endStr))
                    end = int.Parse(match.Groups[3].Value);
                else
                    end = start + 6;
                haveRange = true;
                L.Debug($"JSON path '{jsonPath}' has range [{start}:{end}]");
            }

            var token = root.SelectToken(basePath);

            if (token == null)
            {
                L.Error($"JSON path '{basePath}' not found in response.");
                return double.NaN;
            }

            switch (token.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    return token.Value<double>();

                case JTokenType.String:
                    {
                        var strValue = token.Value<string>();
                        if (haveRange)
                        {
                            var len = strValue.Length;
                            start = Math.Min(start, len);
                            end = Math.Min(end, len);
                            L.Debug($"strValue='{strValue}', start={start}, end={end}");
                            strValue = strValue.Substring(start, end - start);
                            L.Debug($"strValue='{strValue}', start={start}, end={end}");
                        }
                        if (string.IsNullOrEmpty(strValue))
                            return double.NaN;
                        if (strValue.Length > 6)
                            strValue = strValue.Substring(0, 6);
                        return ProgrammableChip.PackAscii6(strValue, _LineNumber);
                    }

                default:
                    L.Error($"JSON value at path '{basePath}' is not a number or string: {token}");
                    return double.NaN;
            }
        }
        catch (Exception ex)
        {
            L.Error($"Failed to parse JSON response: {ex}");
            return double.NaN;
        }
    }

    protected void ParseInputs(string json)
    {
        if (string.IsNullOrEmpty(json))
            return;

        if (!json.StartsWith("'") && !json.StartsWith("{"))
            json = $"'\"{json}\": \".\"'";

        _InputTemplate = new(json, chip, lineNumber);
    }

    protected void ParseOutputs(string json)
    {
        L.Debug($"Parsing outputs from JSON: {json}");
        if (string.IsNullOrEmpty(json))
            return;

        if (!json.StartsWith("'"))
            json = $"'\"{json}\": \".\"'";

        json = json.Trim('\'');

        _OutputTemplate = new TemplateString(json, _Chip, _LineNumber);
        _OutputVariables = [];
        var root = JToken.Parse(json);
        L.Debug($"Parsed outputs JSON: {root}");
        if (root.Type == JTokenType.String)
        {
            var name = root.Value<string>();
            _OutputVariables[name] = MakeOutputVariable(name);
        }
        foreach (var property in root.Children<JProperty>())
        {
            var name = property.Name;
            var value = property.Value;
            L.Debug($"Parsing output property: {name} = {value}");
            if (value.Type == JTokenType.String)
            {
                if (value.Value<string>() == "$success")
                    _SuccessOutput = MakeOutputVariable(name);
                else
                    _OutputVariables[name] = MakeOutputVariable(name);
            }
            else
                L.Error($"Output value for '{name}' is not a string: {value}");
        }
    }

    protected void SetOutputs(bool isSuccess, string response)
    {
        if (_OutputTemplate == null)
            return;

        var successIndex = _SuccessOutput?.GetVariableIndex(ProgrammableChip._AliasTarget.Register) ?? -1;
        if (successIndex >= 0 && !isSuccess)
        {
            L.Debug($"Setting success output '{_SuccessOutput._Alias}' to {(isSuccess ? 1.0 : 0.0)}");
            _Chip._Registers[successIndex] = 0;
            // in case we have a separate success output, don't overwrite the other outputs on failure
            return;
        }

        var outputs = JToken.Parse(_OutputTemplate.GetString());
        JToken root = null;

        try
        {
            root = JToken.Parse(response);
        }
        catch (Exception ex)
        {
            L.Error($"Failed to parse JSON response: {ex}");
        }

        try
        {
            Dictionary<int, double> outputValues = [];

            foreach (var property in outputs.Children<JProperty>())
            {
                var name = property.Name;
                var path = property.Value.Value<string>();
                if (path == "$success")
                    continue;
                var index = _OutputVariables[name].GetVariableIndex(ProgrammableChip._AliasTarget.Register);
                outputValues[index] = GetJsonValue(root, path);
            }

            foreach (var kvp in outputValues)
                _Chip._Registers[kvp.Key] = kvp.Value;
        }
        catch (Exception ex)
        {
            L.Error($"Failed to set output values: {ex}");
            isSuccess = false;
        }
        if (successIndex >= 0)
            _Chip._Registers[successIndex] = isSuccess ? 1.0 : 0.0;
    }

}

public abstract class BaseHTTPRequestOperation : BaseHTTPOperation
{
    protected HashSet<Task<HttpResponseMessage>> _RequestTasks = [];
    public bool IsFireAndForget => _OutputTemplate == null;

    public BaseHTTPRequestOperation(ProgrammableChip chip, int lineNumber) : base(chip, lineNumber)
    { }

    public virtual Task<HttpResponseMessage> MakeRequest()
    {
        throw new NotImplementedException("MakeRequest must be implemented in derived classes.");
    }

    public override int Execute(int index)
    {
        try
        {
            if (IsFireAndForget)
            {
                var completedTasks = _RequestTasks.Where(t => t.IsCompleted).ToList();
                foreach (var task in completedTasks)
                    _RequestTasks.Remove(task);
                _RequestTasks.Add(MakeRequest());
                return index + 1;
            }

            if (_RequestTasks.Count == 0)
            {
                _RequestTasks.Add(MakeRequest());
                return -index; // yield until current request is done
            }

            var request = _RequestTasks.First();

            if (request != null && request.IsCompleted)
            {
                _RequestTasks.Remove(request);
                bool isSuccess = false;
                string content = null;
                if (request.Exception != null)
                    L.Error($"HTTP request failed: {request.Exception}");
                else
                {
                    var response = request.Result;
                    var contentTask = response.Content.ReadAsStringAsync();
                    contentTask.Wait();
                    content = contentTask.Result;
                    isSuccess = response.IsSuccessStatusCode;
                }
                SetOutputs(isSuccess, content);
                return index + 1;
            }
        }
        catch (Exception ex)
        {
            L.Error($"HTTP request failed: {ex}");
            SetOutputs(false, null);
        }

        return -index; // yield until current request is done
    }
}

public class HTTPGetOperation : BaseHTTPRequestOperation
{
    public HTTPGetOperation(ProgrammableChip chip, int lineNumber, List<string> tokens) : base(chip, lineNumber)
    {
        if (tokens.Count < 2 || tokens.Count > 3)
            throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);

        UrlTemplate = new TemplateString(tokens[1], chip, lineNumber);

        if (tokens.Count == 3)
            ParseOutputs(tokens[2]);
    }

    public override Task<HttpResponseMessage> MakeRequest()
    {
        var url = UrlTemplate.GetString();
        L.Debug($"HTTP GET url={url}");
        return Client.GetAsync(url);
    }

}

public class HTTPPostOperation : BaseHTTPRequestOperation
{
    public HTTPPostOperation(ProgrammableChip chip, int lineNumber, List<string> tokens) : base(chip, lineNumber)
    {
        L.Debug($"HTTP POST tokens: {tokens.Count} {string.Join(", ", tokens)}");
        if (tokens.Count < 3 || tokens.Count > 4)
            throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);

        UrlTemplate = new TemplateString(tokens[1], chip, lineNumber);

        ParseInputs(tokens[2]);

        if (tokens.Count == 4)
            ParseOutputs(tokens[3]);
    }

    public override Task<HttpResponseMessage> MakeRequest()
    {
        var url = UrlTemplate.GetString();
        var payload = _InputTemplate.GetString();
        L.Debug($"HTTP POST url={url}, payload={payload}");
        var content = payload == null
               ? null
               : new StringContent(payload, Encoding.UTF8, "application/json");
        return Client.PostAsync(url, content);
    }
}
