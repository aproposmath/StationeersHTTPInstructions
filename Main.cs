namespace StationeersHTTPInstructions;

using System;
using BepInEx;
using HarmonyLib;

[BepInPlugin(ThisModInfo.ModID, ThisModInfo.AssemblyName, ThisModInfo.Version)]
public class HTTPInstructions : BaseUnityPlugin
{
    public const string PluginGuid = ThisModInfo.ModID;
    public const string PluginName = ThisModInfo.AssemblyName;
    public const string PluginVersion = ThisModInfo.Version;
    private Harmony _harmony;

    private void Awake()
    {
        try
        {
            L.SetLogger(this.Logger);
            L.Info($"Awake {ThisModInfo.Info}");

            _harmony = new Harmony(ThisModInfo.ModID);
            _harmony.PatchAll();
        }
        catch (Exception ex)
        {
            L.Error($"Error during init of {ThisModInfo.Info}: {ex}");
        }
    }

    private void OnDestroy()
    {
#if DEBUG
        if (!ModUtils.IsLoadedByScriptEngine(typeof(HTTPInstructions)))
            return;
        L.Info($"OnDestroy of ${ThisModInfo.Info}, cleaning up patches");
        _harmony.UnpatchSelf();
        HTTPOnGetOperation.Cleanup();
#endif
    }
}
