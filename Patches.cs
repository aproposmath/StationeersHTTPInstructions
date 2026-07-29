namespace StationeersHTTPInstructions;

using System;
using System.Collections.Generic;
using System.Text;

using HarmonyLib;

using Assets.Scripts.Objects.Electrical;

[HarmonyPatch(typeof(ProgrammableChip._LineOfCode))]
[HarmonyPatch(MethodType.Constructor)]
[HarmonyPatch([typeof(ProgrammableChip), typeof(string), typeof(int)])]
public static class LineOfCodeCtorPatch
{
    public static List<string> Tokenize(string line, int lineNumber)
    {
        L.Debug($"Tokenizing line {lineNumber}: {line}");
        var result = new List<string>();

        var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            string token = parts[i];

            if (token.StartsWith("\"") || token.StartsWith("'"))
            {
                char quote = token[0];
                var sb = new StringBuilder(token);

                bool escaped = false;
                while (true)
                {
                    bool closed = false;

                    for (int j = 1; j < sb.Length; j++)
                    {
                        char c = sb[j];

                        if (escaped)
                        {
                            escaped = false;
                            continue;
                        }

                        if (c == '\\')
                        {
                            escaped = true;
                            continue;
                        }

                        if (c == quote)
                        {
                            closed = true;
                            break;
                        }
                    }

                    if (closed)
                        break;

                    if (++i >= parts.Length)
                        throw new Exception("Unterminated string.");

                    sb.Append(' ');
                    sb.Append(parts[i]);
                }

                result.Add(sb.ToString());
                continue;
            }


            if ((token.StartsWith("HASH(") || token.StartsWith("STR(")) && !token.EndsWith("\")"))
            {
                var sb = new StringBuilder(token);

                while (true)
                {
                    if (++i >= parts.Length)
                        throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.Unknown, lineNumber);

                    sb.Append(' ');
                    sb.Append(parts[i]);

                    if (parts[i].EndsWith(")"))
                        break;
                }

                result.Add(sb.ToString());
                continue;
            }

            if (token.StartsWith("#"))
                break;

            result.Add(token);
        }

        return result;
    }

    static void Prefix(
        ref string lineOfCode,
        int lineNumber)
    {
        try
        {
            L.Debug($"LineOfCodeCtorPatch Prefix for line {lineNumber}: {lineOfCode}");
            var trimmed_line = lineOfCode.Trim();
            if (!trimmed_line.StartsWith("http"))
                return; // Run original constructor

            L.Debug($"Line {lineNumber} is a potential custom HTTP instruction: {lineOfCode}");

            string operationName = trimmed_line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)[0];
            if (BaseHTTPOperation.OperationTypes.ContainsKey(operationName))
            {
                L.Debug($"Line {lineNumber} is a custom HTTP instruction: {lineOfCode}");
                lineOfCode = "# __HTTP_INSTRUCTION__ " + lineOfCode; // Comment out the original line to prevent the original constructor from running
            }
        }
        catch (Exception ex)
        {
            L.Error($"Error in LineOfCodeCtorPatch for line {lineNumber}: {ex}");
            L.Error(ex.StackTrace);
            return;
        }
    }

    static void Postfix(
        ProgrammableChip._LineOfCode __instance,
        ProgrammableChip chip,
        string lineOfCode,
        int lineNumber)
    {
        try
        {
            if (!__instance.LineOfCode.StartsWith("# __HTTP_INSTRUCTION__ "))
                return; // Run original constructor

            var originalLine = __instance.LineOfCode.Substring("# __HTTP_INSTRUCTION__ ".Length);

            L.Debug($"Creating custom operation for line {lineNumber}: {originalLine}");

            string operationName = originalLine.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)[0];
            var type = BaseHTTPOperation.OperationTypes[operationName];

            var tokens = Tokenize(originalLine, lineNumber);
            L.Debug($"Tokens for line {lineNumber}: {string.Join(", ", tokens)}");
            var operation = (BaseHTTPOperation)Activator.CreateInstance(type, chip, lineNumber, tokens);
            L.Debug($"__instance before setting fields: {__instance}");
            typeof(ProgrammableChip._LineOfCode).GetField("Operation", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).SetValue(__instance, operation);
            typeof(ProgrammableChip._LineOfCode).GetField("LineOfCode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).SetValue(__instance, lineOfCode);

            L.Debug($"Created operation {operationName} for line {lineNumber}: {__instance.Operation}");
            L.Debug($"LineOfCode: {__instance.LineOfCode}");
        }
        catch (Exception ex)
        {
            L.Error($"Error in LineOfCodeCtorPatch Postfix for line {lineNumber}: {ex}");
            L.Error(ex.StackTrace);
        }
    }

    static void CustomConstructor(
        ProgrammableChip._LineOfCode instance,
        ProgrammableChip chip,
        string lineOfCode,
        int lineNumber)
    {
        // Your replacement implementation here.
        // Must initialize all fields the original constructor normally does.
    }

    static bool ShouldUseCustomParser(string line)
    {
        return line.StartsWith("MYOP");
    }
}