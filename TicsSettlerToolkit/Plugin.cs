using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace TicsSettlerToolkit;

[BepInPlugin(Guid, Name, Version)]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "com.tic0311.ticssettlertoolkit";
    public const string Name = "Tic's Settler Toolkit";
    public const string Version = "1.0.0";

    public override void Load()
    {
        ToolkitBridge.Log = Log;
        ToolkitBridge.Harmony = new Harmony(Guid);
        ToolkitBridge.LoadLearnedTraitCatalog();
        ToolkitBridge.InstallHooks();
        
        ClassInjector.RegisterTypeInIl2Cpp<ToolkitBehaviour>();
        this.AddComponent<ToolkitBehaviour>();
        
        Log.LogInfo("Tic's Settler Toolkit loaded. Press F8 in-game.");
    }
}

public class ToolkitBehaviour : MonoBehaviour
{
    public ToolkitBehaviour(IntPtr ptr) : base(ptr) { }

    private void OnGUI()
    {
        ToolkitBridge.OnGUI();
    }
}

internal static class ToolkitBridge
{
    public static ManualLogSource? Log;
    public static Harmony? Harmony;

    private static object? clan;
    private static object? playerUnitProvider;
    private static object? entityComponentSystem;
    private static object? settlementItemContainer;
    private static bool dumpedItemContainerShape;
    private static readonly List<object> playerUnits = new();
    private static readonly List<object> statuses = new();
    private static readonly List<object> inventories = new();
    private static readonly List<object> statusReaders = new();
    private static readonly List<object> inventoryReaders = new();
    private static readonly List<object> affecterReaders = new();
    private static readonly List<object> affecters = new();
    private static readonly List<object> skillReaders = new();
    private static readonly List<object> skills = new();
    private static readonly List<string> playerNames = new();

    public static bool GodMode;
    public static bool InfiniteEnergy;
    public static bool NoHunger;
    public static bool ZeroStress;

    private static bool menuOpen;
    private static int tab;
    private static int selectedCharacter;
    private static int loadedCharacter = -1;
    private static readonly string[] tabs = { "SETTLER", "TRAITS", "INVENTORY", "WORLD" };
    private static float windowX = -1f, windowY = -1f;
    private static float windowW = 1040f, windowH = 720f;
    private const float MinWindowW = 900f, MinWindowH = 650f;
    private static bool dragging;
    private static bool resizing;
    private static float dragOffsetX, dragOffsetY;
    private static float resizeStartMouseX, resizeStartMouseY, resizeStartW, resizeStartH;
    private static readonly Dictionary<string,string> inventoryEdits = new();
    private static readonly Dictionary<string,string> attributeEdits = new();
    private static readonly Dictionary<string,string> buildEdits = new();
    private static int loadedAttributesCharacter = -1;
    private static int loadedTraitsCharacter = -1;
    private static readonly List<string> mainTraitCatalog = new();
    private static readonly List<string> subTraitCatalog = new();
    private static readonly HashSet<string> learnedMainTraits = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> learnedSubTraits = new(StringComparer.OrdinalIgnoreCase);
    private static bool learnedCatalogLoaded;
    private static bool traitCatalogAttempted;
    private static int inventoryScroll;
    private static object? cachedTraitSheet;
    private static object? cachedDataSheetManager;
    private static string traitPickerKind = "";
    private static string traitPickerOldKey = "";
    private static int traitPickerPage;
        private static readonly Dictionary<string,string> settlementEdits = new();
    private static int settlementScroll = 0;

    private static string goldText = "";
    private static string healthText = "";
    private static string energyText = "";
    private static string hungerText = "";
    private static string stressText = "";
    private static string levelText = "";
    private static string xpText = "";
    private static string mainSpText = "";
    private static string subSpText = "";

    private static Type? guiType, rectType, eventType, timeType, screenType, resourcesType, colorType, texture2DType, inputType, cursorType;
    private static MethodInfo? guiLabel, guiBox, guiButton, guiTextField, guiToggle, guiDrawTexture;
    private static PropertyInfo? eventCurrent, eventTypeProp, eventKeyCodeProp, eventMouseProp, eventButtonProp, timeScaleProp;
    private static MethodInfo? eventUseMethod;
    private static PropertyInfo? guiColorProp, guiBackgroundColorProp, guiContentColorProp, guiSkinProp, whiteTextureProp, cursorVisibleProp;
    private static bool skinStyled;
    private static bool dumpedUnitShape;
    private static readonly HashSet<string> dumpedProviders = new();
    private static readonly HashSet<string> dumpedReaderTypes = new();
    private static readonly HashSet<string> dumpedComponentTypes = new();
    private static bool dumpedMutationApis;
    private static readonly HashSet<string> dumpedReaderShapes = new();

    public static int CharacterCount => playerUnits.Count;

    public static void InstallHooks()
    {
        int patched = 0;
        var traitSheetType=GameTypes().FirstOrDefault(t=>t.FullName=="Refactor.Util.TraitSheet" || t.Name=="TraitSheet");
        int traitSheetOwnerHooks=0;
        foreach (var t in GameTypes())
        {
            try
            {
                if(t.FullName=="Refactor.Main.EntityComponent" || t.Name=="EntityComponent")
                {
                    var postfix=new HarmonyMethod(typeof(ToolkitBridge).GetMethod(nameof(CaptureEntityComponentSystem),BindingFlags.Static|BindingFlags.NonPublic)!);
                    foreach(var ctor in t.GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                    {
                        try { Harmony!.Patch(ctor,postfix:postfix); } catch {}
                    }
                }

                if(t.FullName=="Refactor.Util.DataSheetManager" || t.Name=="DataSheetManager")
                {
                    var captureManager=new HarmonyMethod(typeof(ToolkitBridge).GetMethod(nameof(CaptureDataSheetManager),BindingFlags.Static|BindingFlags.NonPublic)!);
                    foreach(var dm in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                        .Where(m=>m.GetParameters().Length==0 && (m.Name=="Awake" || m.Name=="Start" || m.Name=="Init" || m.Name=="Initialize" || m.Name=="Load")))
                    {
                        try { Harmony!.Patch(dm,postfix:captureManager); } catch {}
                    }
                }

                if(t.FullName=="Refactor.Util.TraitSheet" || t.Name=="TraitSheet")
                {
                    var captureTraitSheet=new HarmonyMethod(typeof(ToolkitBridge).GetMethod(nameof(CaptureTraitSheet),BindingFlags.Static|BindingFlags.NonPublic)!);
                    foreach(var tm in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                        .Where(m=>m.Name=="GetTraits" || m.Name=="TryGetTraitData"))
                    {
                        try { Harmony!.Patch(tm,postfix:captureTraitSheet); } catch {}
                    }
                }

                // Capture TraitSheet from the game's actual owner/accessor instead of searching Unity.
                // Any method/property accessor that returns TraitSheet can safely hand us the live object.
                if(traitSheetType!=null)
                {
                    var captureTraitSheetResult=new HarmonyMethod(typeof(ToolkitBridge).GetMethod(nameof(CaptureTraitSheetResult),BindingFlags.Static|BindingFlags.NonPublic)!);
                    foreach(var tm in t.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))
                    {
                        if(tm.DeclaringType==traitSheetType) continue;
                        if(tm.ReturnType==typeof(void)) continue;
                        if(!(traitSheetType.IsAssignableFrom(tm.ReturnType) || tm.ReturnType.FullName==traitSheetType.FullName)) continue;
                        try
                        {
                            Harmony!.Patch(tm,postfix:captureTraitSheetResult);
                            traitSheetOwnerHooks++;
                            Log?.LogDebug($"[TST-TRAIT-SHEET] owner hook {t.FullName}::{tm.Name} -> {tm.ReturnType.FullName}");
                        }
                        catch {}
                    }
                }

                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if ((m.Name == "get_ClanGold" || m.Name == "AddClanGold" || m.Name == "set_ClanGold") && !m.IsStatic)
                    {
                        Harmony!.Patch(m, postfix: new HarmonyMethod(typeof(ToolkitBridge).GetMethod(nameof(CaptureClan), BindingFlags.Static | BindingFlags.NonPublic)!));
                    }

                    if (m.Name == "GetAllPlayerUnits")
                    {
                        try { Harmony!.Patch(m, postfix: new HarmonyMethod(typeof(ToolkitBridge).GetMethod(nameof(CapturePlayerList), BindingFlags.Static | BindingFlags.NonPublic)!)); } catch {}
                    }

                    if (m.Name == "GetPlayerUnit" && m.ReturnType.Name.Contains("UnitEntity"))
                    {
                        try { Harmony!.Patch(m, postfix: new HarmonyMethod(typeof(ToolkitBridge).GetMethod(nameof(CapturePlayerUnitResult), BindingFlags.Static | BindingFlags.NonPublic)!)); } catch {}
                    }

                    if ((m.Name == "AddPlayerUnit" || m.Name == "OnSpawnPlayerUnit" || m.Name == "InitPlayerUnitUI") &&
                        m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType.Name.Contains("UnitEntity"))
                    {
                        try { Harmony!.Patch(m, postfix: new HarmonyMethod(typeof(ToolkitBridge).GetMethod(nameof(CapturePlayerUnitArg), BindingFlags.Static | BindingFlags.NonPublic)!)); } catch {}
                    }
                }

                // Player status/inventory are resolved from the game's actual player-unit list.
                // We intentionally do not capture every StatusComponent/InventoryComponent in the scene.
            }
            catch (Exception e)
            {
                Log?.LogDebug(e);
            }
        }

        InstallInputBlockHooks();
        DumpComponentApis();
        Log?.LogInfo($"Installed Treasure Tool hooks. TraitSheet owner hooks: {traitSheetOwnerHooks}");
    }

    private static void PatchCaptureMethods(Type t, string postfixName)
    {
        var postfix = new HarmonyMethod(typeof(ToolkitBridge).GetMethod(postfixName, BindingFlags.Static | BindingFlags.NonPublic)!);
        var useful = new HashSet<string>(StringComparer.Ordinal) { "Awake", "Start", "Init", "Initialize", "Load", "Update", "Tick", "AddItem", "RemoveItem" };

        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!useful.Contains(m.Name)) continue;
            try { Harmony!.Patch(m, postfix: postfix); } catch { }
        }
    }


    private static void DumpComponentApis()
    {
        try
        {
            foreach(var t in GameTypes().Where(t=>t.FullName=="Refactor.EntityExtensions" || t.Name=="EntityExtensions"))
            {
                foreach(var m in t.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))
                {
                    if(!(m.Name.Contains("Component")||m.Name.Contains("Reader"))) continue;
                    var gps=m.IsGenericMethodDefinition
                        ? string.Join(",",m.GetGenericArguments().Select(g=>$"{g.Name}[{string.Join("|",g.GetGenericParameterConstraints().Select(x=>x.FullName))}]"))
                        : "";
                    Log?.LogInfo($"[TST-API] {t.FullName}::{m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))}) -> {m.ReturnType.FullName} generic={m.IsGenericMethodDefinition} gp={gps}");
                }
            }
        }
        catch(Exception e){ Log?.LogWarning($"[TST-API] scan failed: {e.Message}"); }
    }

    private static void InstallInputBlockHooks()
    {
        try
        {
            var input=FindType("UnityEngine.Input");
            if(input==null) return;

            var prefix=new HarmonyMethod(typeof(ToolkitBridge).GetMethod(nameof(BlockMouseInputPrefix),BindingFlags.Static|BindingFlags.NonPublic)!);
            foreach(var name in new[]{"GetMouseButton","GetMouseButtonDown","GetMouseButtonUp"})
            {
                foreach(var m in input.GetMethods(BindingFlags.Public|BindingFlags.Static).Where(m=>m.Name==name && m.ReturnType==typeof(bool)))
                {
                    try { Harmony!.Patch(m,prefix:prefix); } catch {}
                }
            }
        }
        catch(Exception e){ Log?.LogDebug(e); }
    }

    private static bool BlockMouseInputPrefix(ref bool __result)
    {
        if(!menuOpen) return true;
        try
        {
            var input=FindType("UnityEngine.Input");
            var mp=input?.GetProperty("mousePosition",BindingFlags.Public|BindingFlags.Static)?.GetValue(null);
            if(mp==null) return true;

            float mx=Convert.ToSingle(GetMember(mp,"x") ?? -1f);
            float sy=Convert.ToSingle(GetMember(mp,"y") ?? -1f);
            float sh=Convert.ToSingle(screenType?.GetProperty("height",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) ?? 720);
            float my=sh-sy; // Unity Input origin is bottom-left; IMGUI origin is top-left.
            float w=windowW,h=windowH;

            if(mx>=windowX && mx<=windowX+w && my>=windowY && my<=windowY+h)
            {
                __result=false;
                return false;
            }
        }
        catch{}
        return true;
    }

    private static IEnumerable<Type> GameTypes()
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = a.GetTypes(); } catch { continue; }
            foreach (var t in types) yield return t;
        }
    }

    private static void CaptureClan(object __instance)
    {
        if(__instance==null) return;
        var t=__instance.GetType();
        if(t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Any(m=>m.Name=="AddClanGold") ||
           t.GetProperty("ClanGold",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!=null)
        {
            if(!ReferenceEquals(clan,__instance))
            {
                clan=__instance;
                Log?.LogInfo($"[TST-GOLD] Captured clan owner {t.FullName}");
            }
        }
    }

    private static void CaptureSettlementItemContainer(object __result)
    {
        if(__result==null) return;
        settlementItemContainer=__result;
        if(!dumpedItemContainerShape)
        {
            dumpedItemContainerShape=true;
            DumpSettlementItemContainerShape(__result);
        }
    }

    private static void DumpSettlementItemContainerShape(object container)
    {
        var t=container.GetType();
        Log?.LogInfo($"[TST-SETTLEMENT] Item container {t.FullName}");
        try
        {
            foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                Log?.LogInfo($"[TST-SETTLEMENT] PROP {p.Name}:{p.PropertyType.FullName}");
            foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                var n=m.Name.ToLowerInvariant();
                if(n.Contains("all")||n.Contains("item")||n.Contains("entity")||n.Contains("value")||n.Contains("enumer")||n.Contains("dictionary"))
                    Log?.LogInfo($"[TST-SETTLEMENT] METHOD {m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))})->{m.ReturnType.FullName}");
            }
        } catch{}
    }

    private static void CaptureDataSheetManager(object __instance)
    {
        if(__instance==null) return;
        cachedDataSheetManager=__instance;
        TryCaptureTraitSheetFromManager(__instance);
    }

    private static void ResolveTraitSheetFromDataSheetManager()
    {
        if(cachedTraitSheet!=null) return;

        var managerType=GameTypes().FirstOrDefault(t=>t.FullName=="Refactor.Util.DataSheetManager" || t.Name=="DataSheetManager");
        if(managerType==null) return;

        if(cachedDataSheetManager==null)
        {
            // DataSheetManager is a persistent Unity component and may not be returned by the
            // normal scene-only FindObjectsOfType path. Resolve this exact type once from Resources,
            // including inactive / DontDestroyOnLoad objects. This is a single targeted lookup.
            try
            {
                if(resourcesType==null) resourcesType=FindType("UnityEngine.Resources");
                var all=resourcesType?.GetMethods(BindingFlags.Static|BindingFlags.Public)
                    .FirstOrDefault(m=>m.Name=="FindObjectsOfTypeAll" &&
                        m.GetParameters().Length==1 &&
                        m.GetParameters()[0].ParameterType==typeof(Type))
                    ?.Invoke(null,new object[]{managerType});

                foreach(var obj in EnumerateAny(all))
                {
                    if(obj==null) continue;
                    cachedDataSheetManager=obj;
                    Log?.LogInfo($"[TST-TRAIT-SHEET] Found DataSheetManager via Resources.FindObjectsOfTypeAll");
                    break;
                }
            }
            catch(Exception ex)
            {
                Log?.LogWarning($"[TST-TRAIT-SHEET] DataSheetManager Resources lookup failed: {ex.GetBaseException().Message}");
            }

            // Scene lookup remains a cheap fallback.
            if(cachedDataSheetManager==null)
            {
                try
                {
                    cachedDataSheetManager=FindObjectsOfType(managerType).FirstOrDefault();
                    if(cachedDataSheetManager!=null)
                        Log?.LogInfo("[TST-TRAIT-SHEET] Found DataSheetManager via scene lookup");
                }
                catch {}
            }
        }

        if(cachedDataSheetManager!=null)
            TryCaptureTraitSheetFromManager(cachedDataSheetManager);
        else
            Log?.LogWarning("[TST-TRAIT-SHEET] DataSheetManager instance not found");
    }

    private static void TryCaptureTraitSheetFromManager(object manager)
    {
        if(cachedTraitSheet!=null) return;

        try
        {
            // The interop log shows DataSheetManager::_trait as the actual TraitSheet backing member.
            var trait=GetMember(manager,"_trait");
            if(trait!=null)
            {
                Log?.LogInfo($"[TST-TRAIT-SHEET] Direct DataSheetManager._trait capture");
                CaptureTraitSheet(trait);
                return;
            }

            Log?.LogWarning($"[TST-TRAIT-SHEET] DataSheetManager found but _trait returned null ({manager.GetType().FullName})");

            // Defensive fallbacks for wrapper/backing-field naming.
            foreach(var name in new[]{"Trait","trait","_Trait_k__BackingField","TraitSheet"})
            {
                trait=GetMember(manager,name);
                if(trait==null) continue;
                var n=trait.GetType().FullName ?? trait.GetType().Name;
                if(n.Contains("TraitSheet",StringComparison.Ordinal))
                {
                    Log?.LogInfo($"[TST-TRAIT-SHEET] Direct DataSheetManager.{name} capture");
                    CaptureTraitSheet(trait);
                    return;
                }
            }
        }
        catch(Exception ex)
        {
            Log?.LogWarning($"[TST-TRAIT-SHEET] DataSheetManager direct capture failed: {ex.GetBaseException().Message}");
        }
    }

    private static void CaptureTraitSheetResult(object __result)
    {
        if(__result==null) return;
        var n=__result.GetType().FullName ?? __result.GetType().Name;
        if(!(n=="Refactor.Util.TraitSheet" || n.EndsWith(".TraitSheet",StringComparison.Ordinal))) return;
        CaptureTraitSheet(__result);
    }

    private static void CaptureTraitSheet(object __instance)
    {
        if(__instance==null) return;
        if(!ReferenceEquals(cachedTraitSheet,__instance))
        {
            cachedTraitSheet=__instance;
            traitCatalogAttempted=false;
            mainTraitCatalog.Clear();
            subTraitCatalog.Clear();
            Log?.LogInfo($"[TST-TRAIT-SHEET] Captured live {__instance.GetType().FullName}");
        }
    }

    private static void CaptureEntityComponentSystem(object __instance)
    {
        if(entityComponentSystem==null)
        {
            entityComponentSystem=__instance;
            Log?.LogInfo($"[TST-ECS] Captured entity component system: {__instance.GetType().FullName}");
        }
    }

    private static void CapturePlayerList(object __result)
    {
        if(__result is not IEnumerable e) return;
        bool changed=false;
        foreach(var u in e)
        {
            if(u==null) continue;
            if(!u.GetType().Name.Contains("UnitEntity")) continue;
            if(!playerUnits.Contains(u)) { playerUnits.Add(u); changed=true; }
        }
        if(changed) RebuildPlayerCaches();
    }

    private static void CapturePlayerUnitResult(object __result)
    {
        if(__result==null || !__result.GetType().Name.Contains("UnitEntity")) return;
        AddCapturedPlayerUnit(__result);
    }

    private static void CapturePlayerUnitArg(object __0)
    {
        if(__0==null || !__0.GetType().Name.Contains("UnitEntity")) return;
        AddCapturedPlayerUnit(__0);
    }

    private static void AddCapturedPlayerUnit(object unit)
    {
        if(!playerUnits.Contains(unit))
        {
            playerUnits.Add(unit);
            RebuildPlayerCaches();
        }
        if(!dumpedUnitShape)
        {
            dumpedUnitShape=true;
            DumpUnitShape(unit);
        }
    }

    private static void DumpUnitShape(object unit)
    {
        try
        {
            var t=unit.GetType();
            Log?.LogInfo($"[TST-DIAG] UnitEntity type: {t.FullName}");
            foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                if(IsInterestingMember(p.Name,p.PropertyType.Name))
                    Log?.LogInfo($"[TST-DIAG] PROP {p.Name} : {p.PropertyType.FullName}");
            foreach(var fld in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                if(IsInterestingMember(fld.Name,fld.FieldType.Name))
                    Log?.LogInfo($"[TST-DIAG] FIELD {fld.Name} : {fld.FieldType.FullName}");
            foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                if(IsInterestingMember(m.Name,m.ReturnType.Name))
                    Log?.LogInfo($"[TST-DIAG] METHOD {m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.Name))}) -> {m.ReturnType.Name}");
        }
        catch(Exception e){ Log?.LogWarning($"[TST-DIAG] dump failed: {e.Message}"); }
    }

    private static bool IsInterestingMember(string name,string typeName)
    {
        var s=(name+" "+typeName).ToLowerInvariant();
        return s.Contains("name")||s.Contains("guid")||s.Contains("component")||s.Contains("status")||
               s.Contains("inventory")||s.Contains("profile")||s.Contains("player")||s.Contains("entity")||
               s.Contains("data")||s.Contains("item");
    }

    private static void CaptureStatus(object __instance)
    {
        if (!statuses.Contains(__instance)) statuses.Add(__instance);
    }

    private static void CaptureInventory(object __instance)
    {
        if (!inventories.Contains(__instance)) inventories.Add(__instance);
    }

    public static void OnGUI()
    {
        try
        {
            if (!EnsureUnityGui()) return;

            if (WasF8Pressed()) {
                menuOpen = !menuOpen;
                if (menuOpen) {
                    DiscoverRuntimeObjects(true);
                    LoadCharacterFields();
                    var g = GetGold(); if (g.HasValue) goldText = g.Value.ToString();
                }
            }


            if (!menuOpen) return;

            DiscoverRuntimeObjects(false);
            HandleWindowDragAndResize();
            ApplyPalette();
            DrawPanel();

            ConsumeMouseInput();
            
            // Force hardware cursor to hide when hovering our UI so only game's custom software cursor shows
            HideHardwareCursorIfHovering();
        }
        catch (Exception e)
        {
            Log?.LogDebug(e);
        }
    }

    private static bool EnsureUnityGui()
    {
        if (guiType != null) return true;

        guiType = FindType("UnityEngine.GUI");
        rectType = FindType("UnityEngine.Rect");
        eventType = FindType("UnityEngine.Event");
        timeType = FindType("UnityEngine.Time");
        screenType = FindType("UnityEngine.Screen");
        resourcesType = FindType("UnityEngine.Resources");
        colorType = FindType("UnityEngine.Color");
        texture2DType = FindType("UnityEngine.Texture2D");
        inputType = FindType("UnityEngine.Input");
        cursorType = FindType("UnityEngine.Cursor");
        if (guiType == null || rectType == null || eventType == null) return false;

        guiLabel = FindGuiMethod("Label", 2, typeof(string));
        guiBox = FindGuiMethod("Box", 2, typeof(string));
        guiButton = FindGuiMethod("Button", 2, typeof(string));
        guiTextField = FindGuiMethod("TextField", 2, typeof(string));
        guiToggle = guiType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "Toggle" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType == typeof(bool));
        guiDrawTexture = guiType.GetMethods(BindingFlags.Public|BindingFlags.Static)
            .FirstOrDefault(m => m.Name=="DrawTexture" && m.GetParameters().Length==2 && m.GetParameters()[0].ParameterType==rectType);

        eventCurrent = eventType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
        eventTypeProp = eventType.GetProperty("type", BindingFlags.Public | BindingFlags.Instance);
        eventKeyCodeProp = eventType.GetProperty("keyCode", BindingFlags.Public | BindingFlags.Instance);
        eventMouseProp = eventType.GetProperty("mousePosition", BindingFlags.Public | BindingFlags.Instance);
        eventButtonProp = eventType.GetProperty("button", BindingFlags.Public | BindingFlags.Instance);
        eventUseMethod = eventType.GetMethod("Use", BindingFlags.Public | BindingFlags.Instance);
        timeScaleProp = timeType?.GetProperty("timeScale", BindingFlags.Public | BindingFlags.Static);
        guiColorProp = guiType.GetProperty("color", BindingFlags.Public | BindingFlags.Static);
        guiBackgroundColorProp = guiType.GetProperty("backgroundColor", BindingFlags.Public | BindingFlags.Static);
        guiContentColorProp = guiType.GetProperty("contentColor", BindingFlags.Public | BindingFlags.Static);
        cursorVisibleProp = cursorType?.GetProperty("visible", BindingFlags.Public | BindingFlags.Static);
        guiSkinProp = guiType.GetProperty("skin", BindingFlags.Public | BindingFlags.Static);
        whiteTextureProp = texture2DType?.GetProperty("whiteTexture", BindingFlags.Public|BindingFlags.Static);

        StyleSkin();
        return guiLabel != null && guiBox != null && guiButton != null && guiTextField != null && guiToggle != null;
    }

    private static MethodInfo? FindGuiMethod(string name, int count, Type secondParam)
    {
        return guiType!.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return m.Name == name && p.Length == count && p[0].ParameterType == rectType && p[1].ParameterType == secondParam;
            });
    }

    private static Type? FindType(string fullName)
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = a.GetType(fullName, false);
            if (t != null) return t;
        }
        return null;
    }

    private static object Rect(float x, float y, float w, float h) =>
        Activator.CreateInstance(rectType!, new object[] { x, y, w, h })!;

    private static void Label(float x, float y, float w, float h, string text) =>
        guiLabel!.Invoke(null, new[] { Rect(x, y, w, h), text });

    private static void Box(float x, float y, float w, float h, string text = "") =>
        guiBox!.Invoke(null, new[] { Rect(x, y, w, h), text });

    private static bool Button(float x, float y, float w, float h, string text) =>
        (bool)(guiButton!.Invoke(null, new[] { Rect(x, y, w, h), text }) ?? false);

    private static string TextField(float x, float y, float w, float h, string text) =>
        (string)(guiTextField!.Invoke(null, new[] { Rect(x, y, w, h), text }) ?? text);

    private static bool Toggle(float x, float y, float w, float h, bool value, string text) =>
        (bool)(guiToggle!.Invoke(null, new object[] { Rect(x, y, w, h), value, text }) ?? value);

    private static bool WasF8Pressed()
    {
        var ev = eventCurrent?.GetValue(null);
        if (ev == null) return false;
        return eventTypeProp?.GetValue(ev)?.ToString() == "KeyDown"
            && eventKeyCodeProp?.GetValue(ev)?.ToString() == "F8";
    }

    private static void DrawPanel()
    {
        float w = windowW, h = windowH;
        if (windowX < 0 || windowY < 0) CenterWindow(w,h);
        float x = windowX, y = windowY;

        // Dungeon Settlers-inspired slate panels with restrained brass trim.
        // Windows XP Classic Window Background
        FillRect(x,y,w,h,0.83f,0.81f,0.78f,1f); // Base Classic Gray
        
        // Classic 3D Light Borders (Top/Left)
        FillRect(x,y,w-1,1,1f,1f,1f,1f);
        FillRect(x,y,1,h-1,1f,1f,1f,1f);
        FillRect(x+1,y+1,w-3,1,0.89f,0.89f,0.89f,1f);
        FillRect(x+1,y+1,1,h-3,0.89f,0.89f,0.89f,1f);
        
        // Classic 3D Dark Borders (Bottom/Right)
        FillRect(x,y+h-1,w,1,0f,0f,0f,1f);
        FillRect(x+w-1,y,1,h,0f,0f,0f,1f);
        FillRect(x+1,y+h-2,w-2,1,0.5f,0.5f,0.5f,1f);
        FillRect(x+w-2,y+1,1,h-2,0.5f,0.5f,0.5f,1f);

        SetBackground(0.83f,0.81f,0.78f);
        Box(x,y,w,h,"");

        // Title Bar (Dark Blue Classic)
        FillRect(x+3,y+3,w-6,24,0f,0f,0.6f,1f);
        Label(x+8,y+6,560,30,"TIC'S TREASURE TOOL V2");
        Label(x+31,y+45,560,20,"Settler editing, traits, inventory & world utilities");
        Label(x+w-250,y+45,185,20,$"{playerUnits.Count} SETTLER{(playerUnits.Count==1 ? "" : "S")} READY");

        SetBackground(0.7f,0.1f,0.1f);
        if(Button(x+w-30,y+5,22,20,"X")) menuOpen=false;

        float tx=x+18;
        float tabW=Math.Max(145f,(w-36f-(tabs.Length-1)*6f)/tabs.Length);
        for(int i=0;i<tabs.Length;i++)
        {
            if(i==tab)
            {
                FillRect(tx,y+85,tabW,32,0.9f,0.9f,0.9f,1f);
                FillRect(tx,y+115,tabW,2,0.4f,0.4f,0.4f,1f);
                SetBackground(0.9f,0.9f,0.9f);
            }
            else SetBackground(0.75f,0.73f,0.70f);

            if(Button(tx,y+91,tabW,38,tabs[i])) { tab=i; traitPickerKind=""; }
            tx+=tabW+6;
        }

        FillRect(x+18,y+143,w-36,h-184,0.83f,0.81f,0.78f,1f); // Classic Gray
        FillRect(x+18,y+143,w-36,1,0.5f,0.5f,0.5f,1f); // Inset shadow top
        FillRect(x+18,y+143,1,h-184,0.5f,0.5f,0.5f,1f); // Inset shadow left
        FillRect(x+18,y+143+h-185,w-36,1,1f,1f,1f,1f); // Inset highlight bottom
        FillRect(x+18+w-37,y+143,1,h-184,1f,1f,1f,1f); // Inset highlight right
        SetBackground(0.83f,0.81f,0.78f);
        Box(x+18,y+143,w-36,h-184,"");

        switch(tab)
        {
            case 0: DrawCharacter(x,y); break;
            case 1: DrawAttributes(x,y); break;
            case 2: DrawInventory(x,y); break;
            case 3: DrawGame(x,y); break;
        }

        FillRect(x+18,y+h-38,w-36,26,0.75f,0.73f,0.70f,1f);
        FillRect(x+18,y+h-38,w-36,1,0.5f,0.5f,0.5f,1f); // Inset border
        FillRect(x+18,y+h-13,w-36,1,1f,1f,1f,1f);
        Label(x+30,y+h-34,440,20,"F8 CLOSE  •  DRAG HEADER  •  RESIZE FROM LOWER-RIGHT");
        Label(x+w-305,y+h-34,260,20,playerUnits.Count>0 ? "TOOLKIT ACTIVE" : "WAITING FOR GAME");

        // Classic resize grip
        FillRect(x+w-25,y+h-23,13,2,0.5f,0.5f,0.5f,1f);
        FillRect(x+w-25,y+h-22,13,1,1f,1f,1f,1f);
        FillRect(x+w-20,y+h-18,8,2,0.5f,0.5f,0.5f,1f);
        FillRect(x+w-20,y+h-17,8,1,1f,1f,1f,1f);
        FillRect(x+w-15,y+h-13,3,2,0.5f,0.5f,0.5f,1f);
        FillRect(x+w-15,y+h-12,3,1,1f,1f,1f,1f);

        ApplyPalette();
    }

    private static void CharacterPicker(float x,float y)
    {
        int count=Math.Max(1,CharacterCount);
        SectionTitle(x+36,y+158,Math.Max(620f,windowW-72f),"SELECT SETTLER");
        Label(x+52,y+197,90,24,"Settler");

        SetBackground(0.83f,0.81f,0.78f);
        if(Button(x+146,y+191,38,32,"‹"))
        {
            selectedCharacter=(selectedCharacter-1+count)%count;
            OnSelectedCharacterChanged();
        }

        // TextBox inset
        FillRect(x+194,y+190,310,34,0.95f,0.95f,0.95f,1f);
        FillRect(x+194,y+190,310,1,0.5f,0.5f,0.5f,1f);
        FillRect(x+194,y+190,1,34,0.5f,0.5f,0.5f,1f);
        Label(x+211,y+197,280,24,GetCharacterName(selectedCharacter));

        SetBackground(0.83f,0.81f,0.78f);
        if(Button(x+514,y+191,38,32,"›"))
        {
            selectedCharacter=(selectedCharacter+1)%count;
            OnSelectedCharacterChanged();
        }

        if(Button(x+562,y+191,96,32,"REFRESH"))
            DiscoverRuntimeObjects(true);
        Label(x+570,y+197,250,24,$"{selectedCharacter+1} / {Math.Max(1,CharacterCount)}");
    }

    private static void OnSelectedCharacterChanged()
    {
        LoadCharacterFields();
        inventoryEdits.Clear();
        attributeEdits.Clear();
        loadedAttributesCharacter=-1;
        loadedTraitsCharacter=-1;
        traitPickerKind="";
        inventoryScroll=0;
    }

    private static void DrawCharacter(float x, float y)
    {
        CharacterPicker(x, y);
        if (loadedCharacter != selectedCharacter) LoadCharacterFields();

        SectionTitle(x + 36, y + 238, 610, "VITALS & PROGRESSION");
        float ry = y + 276;
        StatRow(x, ry, "Body Health %", ref healthText, v => SetCharacterStat(selectedCharacter, "HealthBody", v / 100f)); ry += 42;
        StatRow(x, ry, "Energy %", ref energyText, v => SetCharacterStat(selectedCharacter, "Energy", v / 100f)); ry += 42;
        StatRow(x, ry, "Hunger %", ref hungerText, v => SetCharacterStat(selectedCharacter, "Hunger", v / 100f)); ry += 42;
        StatRow(x, ry, "Stress %", ref stressText, v => SetCharacterStat(selectedCharacter, "Stress", v / 100f)); ry += 50;
        StatRow(x, ry, "Level", ref levelText, v => SetCharacterProgress(selectedCharacter, "Level", v)); ry += 42;
        StatRow(x, ry, "Experience", ref xpText, v => SetCharacterProgress(selectedCharacter, "Exp", v)); ry += 42;
        StatRow(x, ry, "Main Skill Points", ref mainSpText, v => SetCharacterProgress(selectedCharacter, "MainSkillPoint", v)); ry += 42;
        StatRow(x, ry, "Sub Skill Points", ref subSpText, v => SetCharacterProgress(selectedCharacter, "SubSkillPoint", v));
    }

    private static void StatRow(float x, float y, string label, ref string text, Action<float> apply)
    {
        Label(x + 58, y, 220, 28, label);
        text = TextField(x + 300, y - 2, 150, 30, text);
        SetBackground(0.31f,0.20f,0.09f);
        if (Button(x + 468, y - 2, 82, 30, "SET") && float.TryParse(text, out var v)) apply(v);
    }

    private static void LoadCharacterFields()
    {
        loadedCharacter = selectedCharacter;
        healthText = PercentText(GetCharacterStat(selectedCharacter, "HealthBody"), healthText);
        energyText = PercentText(GetCharacterStat(selectedCharacter, "Energy"), energyText);
        hungerText = PercentText(GetCharacterStat(selectedCharacter, "Hunger"), hungerText);
        stressText = PercentText(GetCharacterStat(selectedCharacter, "Stress"), stressText);
        levelText = NumberText(GetCharacterProgress(selectedCharacter, "Level"), levelText);
        xpText = NumberText(GetCharacterProgress(selectedCharacter, "Exp"), xpText);
        mainSpText = NumberText(GetCharacterProgress(selectedCharacter, "MainSkillPoint"), mainSpText);
        subSpText = NumberText(GetCharacterProgress(selectedCharacter, "SubSkillPoint"), subSpText);
    }

    private static string PercentText(float? v, string fallback)
    {
        if(!v.HasValue) return fallback;
        var x=v.Value;
        // Dungeon Settlers StatusReader exposes state values in centi-units (e.g. 6139 = 61.39).
        if(Math.Abs(x)>1000f) x/=100f;
        else if(Math.Abs(x)<=1.5f) x*=100f;
        return Math.Round(x,2).ToString("0.##");
    }
    private static string NumberText(float? v, string fallback) => v.HasValue ? v.Value.ToString("0.##") : fallback;

    private static void DrawInventory(float x,float y)
    {
        CharacterPicker(x,y);
        var items=GetInventory(selectedCharacter);

        float panelW=Math.Max(760f,windowW-72f);
        SectionTitle(x+36,y+238,panelW,"CARRIED INVENTORY");
        Label(x+58,y+277,390,22,"ITEM");
        Label(x+Math.Max(520f,windowW-500f),y+277,110,22,"QUANTITY");

        int visible=Math.Max(5,(int)((windowH-360f)/40f));
        int maxOffset=Math.Max(0,items.Count-visible);
        inventoryScroll=Math.Max(0,Math.Min(inventoryScroll,maxOffset));
        HandleInventoryWheel(x+36,y+270,panelW,windowH-330,items.Count,visible);

        float iy=y+307;
        foreach(var item in items.Skip(inventoryScroll).Take(visible))
        {
            // Classic white list item with subtle separator
            FillRect(x+50,iy-3,panelW-28,34,1f,1f,1f,1f);
            FillRect(x+50,iy-3,panelW-28,1,0.9f,0.9f,0.9f,1f);
            Label(x+62,iy+4,Math.Max(360f,panelW-360f),24,FriendlyItemName(item.Name));

            if(!inventoryEdits.TryGetValue(item.Name,out var txt)) txt=item.Amount.ToString();
            float qx=x+panelW-235;
            txt=TextField(qx,iy,88,29,txt);
            inventoryEdits[item.Name]=txt;
            SetBackground(0.83f,0.81f,0.78f);
            if(Button(qx+98,iy,72,29,"SET") && int.TryParse(txt,out var n))
            {
                SetInventoryAmount(item,n);
                inventoryEdits[item.Name]=Math.Max(0,n).ToString();
            }
            iy+=40;
        }

        if(items.Count==0)
            Label(x+58,y+315,panelW-44,55,playerUnits.Count==0 ? "No settlers resolved yet." : "This settler has no readable carried inventory.");
        else if(items.Count>visible)
        {
            Label(x+panelW-48,y+281,36,22,$"{inventoryScroll+1}");
            SetBackground(0.83f,0.81f,0.78f);
            if(Button(x+panelW-48,y+310,28,28,"▲")) inventoryScroll=Math.Max(0,inventoryScroll-1);
            if(Button(x+panelW-48,y+346,28,28,"▼")) inventoryScroll=Math.Min(maxOffset,inventoryScroll+1);
        }
    }

    private static void HandleInventoryWheel(float x,float y,float w,float h,int count,int visible)
    {
        var ev=eventCurrent?.GetValue(null); if(ev==null) return;
        if((eventTypeProp?.GetValue(ev)?.ToString()??"")!="ScrollWheel") return;
        var mp=eventMouseProp?.GetValue(ev); if(mp==null) return;
        float mx=Convert.ToSingle(GetMember(mp,"x")??-1f);
        float my=Convert.ToSingle(GetMember(mp,"y")??-1f);
        if(mx<x||mx>x+w||my<y||my>y+h) return;

        var delta=GetMember(ev,"delta");
        float dy=Convert.ToSingle(GetMember(delta,"y")??0f);
        inventoryScroll=Math.Max(0,Math.Min(Math.Max(0,count-visible),inventoryScroll+(dy>0?1:-1)));
        try{eventUseMethod?.Invoke(ev,null);}catch{}
    }

    private static void DrawAttributes(float x, float y)
    {
        CharacterPicker(x, y);
        SectionTitle(x + 36, y + 238, 430, "ATTRIBUTES");

        string[] attrs={"Strength","Constitution","WillPower","Intelligence","Agility","Perception"};
        if(loadedAttributesCharacter!=selectedCharacter)
        {
            attributeEdits.Clear();
            foreach(var name in attrs)
            {
                var v=GetGeneratedStat(selectedCharacter,name);
                attributeEdits[name]=v?.ToString("0.##") ?? "";
            }
            loadedAttributesCharacter=selectedCharacter;
        }

        float ay=y+282;
        foreach(var name in attrs)
        {
            if(!attributeEdits.TryGetValue(name,out var text)) text="";
            Label(x+58,ay,165,28,name);
            text=TextField(x+225,ay-2,105,30,text);
            attributeEdits[name]=text;
            SetBackground(0.83f,0.81f,0.78f);
            if(Button(x+344,ay-2,72,30,"SET") && float.TryParse(text,out var v))
            {
                SetGeneratedStat(selectedCharacter,name,v);
                attributeEdits[name]=v.ToString("0.##");
            }
            ay+=42;
        }

        EnsureTraitCatalogs();

        float rightX=x+455;
        float rightW=Math.Max(420f,windowW-495f);
        SectionTitle(rightX,y+238,rightW,"TRAITS");

        var unit=At(playerUnits,selectedCharacter);
        var profile=unit==null ? null : GetMember(unit,"_profile");
        string race=ReadProfileRace(profile) ?? "Unknown";
        Label(rightX+22,y+280,rightW-44,24,$"Race: {race}  (locked)");

        var active=GetAllAffecterKeys(selectedCharacter);
        string raceKey=FindRaceTraitKey(active,race);
        var liveGroups=GetActiveTraitGroupsByType(selectedCharacter,raceKey);
        var mains=liveGroups.Main;
        var subs=liveGroups.Sub;
        LearnTraits(mains,subs);

        // Learned traits persist across saves/runs and become selectable on future launches.

        float ty=y+316;
        Label(rightX+22,ty,rightW-44,22,$"MAIN TRAITS ({mains.Count})");
        ty+=26;
        foreach(var key in mains.Take(4))
        {
            DrawTraitRow(rightX+30,ty,rightW-60,"main",key,selectedCharacter);
            ty+=38;
        }
        SetBackground(0.83f,0.81f,0.78f);
        if(mains.Count<4)
        {
            if(Button(rightX+30,ty,150,30,"+ ADD MAIN TRAIT"))
            {
                traitPickerKind="main"; traitPickerOldKey=""; traitPickerPage=0;
            }
        }
        else Label(rightX+30,ty,190,30,"Main trait cap: 4");
        ty+=44;

        Label(rightX+22,ty,rightW-44,22,$"SUB TRAITS ({subs.Count})");
        ty+=26;
        foreach(var key in subs.Take(4))
        {
            DrawTraitRow(rightX+30,ty,rightW-60,"sub",key,selectedCharacter);
            ty+=38;
        }
        SetBackground(0.83f,0.81f,0.78f);
        if(subs.Count<4)
        {
            if(Button(rightX+30,ty,150,30,"+ ADD SUB TRAIT"))
            {
                traitPickerKind="sub"; traitPickerOldKey=""; traitPickerPage=0;
            }
        }
        else Label(rightX+30,ty,190,30,"Sub trait cap: 4");

        if(mainTraitCatalog.Count==0 || subTraitCatalog.Count==0)
            Label(rightX+205,ty,rightW-225,30,$"Known catalog: {mainTraitCatalog.Count} main / {subTraitCatalog.Count} sub");

        DrawTraitPicker(rightX+20,y+300,rightW-40,selectedCharacter);
    }

    private static void DrawTraitRow(float x,float y,float w,string kind,string key,int character)
    {
        string friendly=FriendlyBuildKey(key);
        bool pickerActive = !string.IsNullOrWhiteSpace(traitPickerKind);

        SetBackground(0.9f,0.9f,0.9f);
        if(pickerActive) Box(x,y,Math.Max(190f,w-170f),30,friendly);
        else if(Button(x,y,Math.Max(190f,w-170f),30,friendly))
        {
            traitPickerKind=kind;
            traitPickerOldKey=key;
            traitPickerPage=0;
        }
        SetBackground(0.83f,0.81f,0.78f);
        if(pickerActive) Box(x+w-158,y,76,30,"REPLACE");
        else if(Button(x+w-158,y,76,30,"REPLACE"))
        {
            traitPickerKind=kind;
            traitPickerOldKey=key;
            traitPickerPage=0;
        }
        SetBackground(0.83f,0.81f,0.78f);
        if(pickerActive) Box(x+w-76,y,72,30,"REMOVE");
        else if(Button(x+w-76,y,72,30,"REMOVE"))
        {
            RemoveCharacterTrait(character,key);
            traitPickerKind="";
        }
    }

    private static void DrawTraitPicker(float x,float y,float w,int character)
    {
        if(string.IsNullOrWhiteSpace(traitPickerKind)) return;
        var source=traitPickerKind=="main" ? mainTraitCatalog : subTraitCatalog;
        if(source.Count==0) return;

        const int visible=8;
        int maxOffset=Math.Max(0,source.Count-visible);
        traitPickerPage=Math.Max(0,Math.Min(traitPickerPage,maxOffset));

        float panelH=Math.Min(windowH-330,390f);
        
        // Classic window inset
        FillRect(x,y,w,panelH,0.83f,0.81f,0.78f,1f);
        FillRect(x,y,w-1,1,1f,1f,1f,1f);
        FillRect(x,y,1,panelH-1,1f,1f,1f,1f);
        FillRect(x,y+panelH-1,w,1,0.5f,0.5f,0.5f,1f);
        FillRect(x+w-1,y,1,panelH,0.5f,0.5f,0.5f,1f);
        
        SetBackground(0.83f,0.81f,0.78f);
        Box(x,y,w,panelH,"");
        Label(x+16,y+12,w-100,24,traitPickerKind=="main" ? "SELECT MAIN TRAIT" : "SELECT SUB TRAIT");
        if(Button(x+w-42,y+8,30,28,"X")) { traitPickerKind=""; return; }

        HandleTraitPickerWheel(x,y,w,panelH,source.Count,visible);

        float py=y+46;
        foreach(var key in source.Skip(traitPickerPage).Take(visible))
        {
            if(Button(x+16,py,w-54,32,FriendlyBuildKey(key)))
            {
                Log?.LogInfo($"[TST-TRAIT-UI] picked kind={traitPickerKind} old={traitPickerOldKey} new={key}");
                bool ok=string.IsNullOrWhiteSpace(traitPickerOldKey)
                    ? AddCharacterTrait(character,key)
                    : ReplaceCharacterTrait(character,traitPickerOldKey,key);
                if(ok) traitPickerKind="";
            }
            py+=36;
        }

        if(source.Count>visible)
        {
            if(Button(x+w-34,y+48,22,28,"▲")) traitPickerPage=Math.Max(0,traitPickerPage-1);
            if(Button(x+w-34,y+panelH-38,22,28,"▼")) traitPickerPage=Math.Min(maxOffset,traitPickerPage+1);
            Label(x+w-42,y+panelH/2-12,34,24,$"{traitPickerPage+1}");
        }
    }

    private static void HandleTraitPickerWheel(float x,float y,float w,float h,int count,int visible)
    {
        var ev=eventCurrent?.GetValue(null); if(ev==null) return;
        var kind=eventTypeProp?.GetValue(ev)?.ToString() ?? "";
        if(kind!="ScrollWheel") return;
        var mp=eventMouseProp?.GetValue(ev); if(mp==null) return;
        float mx=Convert.ToSingle(GetMember(mp,"x") ?? -1f);
        float my=Convert.ToSingle(GetMember(mp,"y") ?? -1f);
        if(mx<x||mx>x+w||my<y||my>y+h) return;

        var delta=GetMember(ev,"delta");
        float dy=Convert.ToSingle(GetMember(delta,"y") ?? 0f);
        int maxOffset=Math.Max(0,count-visible);
        traitPickerPage=Math.Max(0,Math.Min(maxOffset,traitPickerPage+(dy>0?1:-1)));
        try{eventUseMethod?.Invoke(ev,null);}catch{}
    }

    private static string FindRaceTraitKey(List<string> active,string race)
    {
        string compact=race.Replace(" ","").Replace("_","");
        var exact=active.FirstOrDefault(k=>k.Replace("AFFECTER_","").Replace("_","").Equals(compact,StringComparison.OrdinalIgnoreCase));
        return exact ?? active.FirstOrDefault() ?? "";
    }

    private sealed class LearnedTraitCatalog
    {
        public int Version { get; set; } = 1;
        public List<string> Main { get; set; } = new();
        public List<string> Sub { get; set; } = new();
    }

    private static string LearnedTraitCatalogPath =>
        Path.Combine(Paths.ConfigPath,"TicsSettlerToolkit","traits.json");

    public static void LoadLearnedTraitCatalog()
    {
        if(learnedCatalogLoaded) return;
        learnedCatalogLoaded=true;
        try
        {
            var path=LearnedTraitCatalogPath;
            if(!File.Exists(path)) return;
            var data=JsonSerializer.Deserialize<LearnedTraitCatalog>(File.ReadAllText(path));
            if(data==null) return;
            foreach(var k in data.Main.Where(IsSelectableRecruitTrait)) learnedMainTraits.Add(k);
            foreach(var k in data.Sub.Where(IsSelectableRecruitTrait)) learnedSubTraits.Add(k);
            Log?.LogInfo($"[TST-TRAIT-LEARN] Loaded {learnedMainTraits.Count} main / {learnedSubTraits.Count} sub learned traits.");
        }
        catch(Exception ex)
        {
            Log?.LogWarning($"[TST-TRAIT-LEARN] Load failed: {ex.GetBaseException().Message}");
        }
    }

    private static void SaveLearnedTraitCatalog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LearnedTraitCatalogPath)!);
            var data=new LearnedTraitCatalog
            {
                Main=learnedMainTraits.OrderBy(FriendlyBuildKey).ToList(),
                Sub=learnedSubTraits.OrderBy(FriendlyBuildKey).ToList()
            };
            File.WriteAllText(LearnedTraitCatalogPath,JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex)
        {
            Log?.LogWarning($"[TST-TRAIT-LEARN] Save failed: {ex.GetBaseException().Message}");
        }
    }

    private static void LearnTraits(IEnumerable<string> mains,IEnumerable<string> subs)
    {
        bool changed=false;
        foreach(var k in mains.Where(IsSelectableRecruitTrait)) changed|=learnedMainTraits.Add(k);
        foreach(var k in subs.Where(IsSelectableRecruitTrait)) changed|=learnedSubTraits.Add(k);
        if(changed)
        {
            SaveLearnedTraitCatalog();
            Log?.LogInfo($"[TST-TRAIT-LEARN] Catalog now {learnedMainTraits.Count} main / {learnedSubTraits.Count} sub.");
        }
    }

    private static void EnsureTraitCatalogs()
    {
        if(traitCatalogAttempted) return;
        traitCatalogAttempted=true;

        var mains=new HashSet<string>(learnedMainTraits,StringComparer.OrdinalIgnoreCase);
        var subs=new HashSet<string>(learnedSubTraits,StringComparer.OrdinalIgnoreCase);

        // Recruit candidate data is safe: it contains actual background/individual recruit traits.
        CollectCandidateTraitCatalogs(clan,mains,subs);

        // Preserve only already-known real traits from current player definitions. Never use the
        // character's generic AffecterReader.GetAll() as a catalog source.
        foreach(var unit in playerUnits)
        {
            var data=GetMember(unit,"Data");
            foreach(var v in EnumerateAny(GetMember(data,"DefaultTraits")))
            {
                var key=v?.ToString()??"";
                if(!key.StartsWith("AFFECTER_") || IsLikelyRaceTrait(key)) continue;
                subs.Add(key);
            }
        }

        // Authoritative source: DataSheetManager owns the live TraitSheet.
        if(cachedTraitSheet==null) ResolveTraitSheetFromDataSheetManager();
        if(cachedTraitSheet!=null)
        {
            CollectTraitSheetAllRarities(cachedTraitSheet,mains,subs);
            CollectTraitSheetCatalog(cachedTraitSheet,mains,subs);
        }
        else
        {
            Log?.LogInfo("[TST-TRAIT-CATALOG] Waiting for live TraitSheet capture; using recruit traits temporarily.");
        }

        LearnTraits(mains,subs);
        mainTraitCatalog.Clear();
        mainTraitCatalog.AddRange(mains.Where(IsSelectableRecruitTrait).OrderBy(FriendlyBuildKey));
        subTraitCatalog.Clear();
        subTraitCatalog.AddRange(subs.Where(IsSelectableRecruitTrait).OrderBy(FriendlyBuildKey));
        Log?.LogInfo($"[TST-TRAIT-CATALOG] FINAL main={mainTraitCatalog.Count} sub={subTraitCatalog.Count} sheet={(cachedTraitSheet!=null)}");
    }

    private static bool IsSelectableRecruitTrait(string key)
    {
        if(string.IsNullOrWhiteSpace(key) || !key.StartsWith("AFFECTER_",StringComparison.OrdinalIgnoreCase)) return false;
        if(IsLikelyRaceTrait(key)) return false;

        // Explicitly exclude system/game-state affecters that leaked into Build 32.
        string s=key.Replace("AFFECTER_","",StringComparison.OrdinalIgnoreCase);
        string[] blocked={
            "PlayerManagement","Difficulty","NewRecruit","Recruit","TreatmentExpectation",
            "Treatment","Clan","Tutorial","Quest","GameOption","Policy","MoodCause",
            "Consumption","Dismissing","Interaction","SkillReset"
        };
        return !blocked.Any(b=>s.Contains(b,StringComparison.OrdinalIgnoreCase));
    }

    private static void CollectTraitSheetAllRarities(object sheet,HashSet<string> mains,HashSet<string> subs)
    {
        try
        {
            var rarityType=GameTypes().FirstOrDefault(t=>t.FullName=="Refactor.Rarity" || (t.Name=="Rarity" && t.IsEnum));
            var getTraits=sheet.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .FirstOrDefault(m=>m.Name=="GetTraits" && m.GetParameters().Length==1);
            if(rarityType==null || getTraits==null) return;

            int rows=0;
            foreach(var name in Enum.GetNames(rarityType))
            {
                object rarity;
                try{rarity=Enum.Parse(rarityType,name);}catch{continue;}
                object? list=null; try{list=getTraits.Invoke(sheet,new[]{rarity});}catch{continue;}
                foreach(var data in EnumerateAny(list))
                {
                    if(data==null) continue;
                    rows++;
                    foreach(var v in EnumerateAny(GetMember(data,"AvailableBackgroundTraits")))
                    {
                        var k=v?.ToString()??""; if(k.StartsWith("AFFECTER_")) mains.Add(k);
                    }
                    foreach(var v in EnumerateAny(GetMember(data,"AvailableIndividualTraits")))
                    {
                        var k=v?.ToString()??""; if(k.StartsWith("AFFECTER_")) subs.Add(k);
                    }
                }
            }
            Log?.LogInfo($"[TST-TRAIT-SHEET] GetTraits(all rarities) rows={rows} main={mains.Count} sub={subs.Count}");
        }
        catch(Exception ex){ Log?.LogWarning($"[TST-TRAIT-SHEET] rarity scan failed: {ex.GetBaseException().Message}"); }
    }

    private static void CollectCandidateTraitCatalogs(object? root,HashSet<string> mains,HashSet<string> subs)
    {
        if(root==null) return;
        foreach(var name in new[]{"AvailableRecruitCandidates","RecruitCandidates","Candidates"})
        {
            var source=GetMember(root,name);
            foreach(var cand in EnumerateAny(source))
            {
                var bg=(GetMember(cand,"BackgroundTrait") ?? "").ToString() ?? "";
                if(bg.StartsWith("AFFECTER_")) mains.Add(bg);

                foreach(var v in EnumerateAny(GetMember(cand,"IndividualTraits")))
                {
                    var key=v?.ToString()??"";
                    if(key.StartsWith("AFFECTER_")) subs.Add(key);
                }
            }
        }

        // ClanDataContainer often wraps a save/data object.
        foreach(var name in new[]{"Data","SaveData","ClanSaveData","Ref"})
        {
            var nested=GetMember(root,name);
            if(nested!=null && !ReferenceEquals(nested,root))
            {
                foreach(var sourceName in new[]{"AvailableRecruitCandidates","RecruitCandidates","Candidates"})
                {
                    var source=GetMember(nested,sourceName);
                    foreach(var cand in EnumerateAny(source))
                    {
                        var bg=(GetMember(cand,"BackgroundTrait") ?? "").ToString() ?? "";
                        if(bg.StartsWith("AFFECTER_")) mains.Add(bg);
                        foreach(var v in EnumerateAny(GetMember(cand,"IndividualTraits")))
                        {
                            var key=v?.ToString()??"";
                            if(key.StartsWith("AFFECTER_")) subs.Add(key);
                        }
                    }
                }
            }
        }
    }

    private static void CollectTraitSheetCatalog(object sheet,HashSet<string> mains,HashSet<string> subs)
    {
        try
        {
            var table=GetMember(sheet,"_traitTable") ?? GetMember(sheet,"TraitTable");
            if(table==null) return;

            var keys=EnumerateAny(GetMember(table,"Keys")).Select(x=>x?.ToString()??"").Where(x=>!string.IsNullOrWhiteSpace(x)).ToList();
            foreach(var key in keys)
            {
                object? data=GetDictionaryValue(table,key);
                if(data==null)
                {
                    try
                    {
                        var tm=sheet.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                            .FirstOrDefault(m=>m.Name=="TryGetTraitData" && m.GetParameters().Length==2);
                        if(tm!=null)
                        {
                            var args=new object?[]{key,null};
                            if(Convert.ToBoolean(tm.Invoke(sheet,args) ?? false)) data=args[1];
                        }
                    }catch{}
                }

                if(data==null) continue;

                string type=(GetMember(data,"AffecterType") ?? GetMember(data,"TraitType") ?? GetMember(data,"Type") ?? "").ToString() ?? "";
                if(type.Contains("Background",StringComparison.OrdinalIgnoreCase) || type.Contains("Main",StringComparison.OrdinalIgnoreCase))
                    mains.Add(key);
                if(type.Contains("Individual",StringComparison.OrdinalIgnoreCase) || type.Contains("Sub",StringComparison.OrdinalIgnoreCase))
                    subs.Add(key);

                foreach(var v in EnumerateAny(GetMember(data,"AvailableBackgroundTraits")))
                {
                    var s=v?.ToString()??""; if(s.StartsWith("AFFECTER_")) mains.Add(s);
                }
                foreach(var v in EnumerateAny(GetMember(data,"AvailableIndividualTraits")))
                {
                    var s=v?.ToString()??""; if(s.StartsWith("AFFECTER_")) subs.Add(s);
                }
            }

            Log?.LogInfo($"[TST-TRAIT-SHEET] {sheet.GetType().FullName} keys={keys.Count}");
        }
        catch(Exception ex){ Log?.LogWarning($"[TST-TRAIT-SHEET] {ex.GetBaseException().Message}"); }
    }

    private static object? GetDictionaryValue(object dict,object key)
    {
        try
        {
            var item=dict.GetType().GetProperty("Item",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(item!=null) return item.GetValue(dict,new[]{key});
        }catch{}

        try
        {
            var m=dict.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .FirstOrDefault(x=>x.Name=="TryGetValue" && x.GetParameters().Length==2);
            if(m!=null)
            {
                var args=new object?[]{key,null};
                if(Convert.ToBoolean(m.Invoke(dict,args) ?? false)) return args[1];
            }
        }catch{}
        return null;
    }

    private static bool IsLikelyRaceTrait(string key)
    {
        var s=key.Replace("AFFECTER_","");
        return s.Equals("Human",StringComparison.OrdinalIgnoreCase) ||
               s.Contains("Lizard",StringComparison.OrdinalIgnoreCase) ||
               s.Contains("Elf",StringComparison.OrdinalIgnoreCase) ||
               s.Contains("Dwarf",StringComparison.OrdinalIgnoreCase) ||
               s.Contains("Orc",StringComparison.OrdinalIgnoreCase);
    }

    private static bool AddCharacterTrait(int i,string key)
    {
        var reader=At(affecterReaders,i);
        var comp=reader==null || reader is MissingComponent ? At(affecters,i) : GetMember(reader,"Ref");
        if(comp==null || comp is MissingComponent) { Log?.LogWarning("[TST-TRAIT-WRITE] component missing"); return false; }

        try
        {
            Log?.LogInfo($"[TST-TRAIT-WRITE] ADD request char={i} key={key}");
            var add=comp.GetType().GetMethod("AddAffecter",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
                null,new[]{typeof(string),typeof(int),typeof(float)},null);
            if(add==null)
                add=comp.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .FirstOrDefault(m=>m.Name=="AddAffecter" && m.GetParameters().Length==3);

            if(add==null) { Log?.LogWarning("[TST-TRAIT-WRITE] AddAffecter(string,int,float) missing"); return false; }

            add.Invoke(comp,new object[]{key,1,0f});
            try { reader?.GetType().GetMethod("Reload",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(reader,null); } catch{}

            bool has=ReaderHasTrait(reader,key);
            Log?.LogInfo($"[TST-TRAIT-WRITE] ADD result key={key} has={has}");
            return has;
        }
        catch(Exception ex)
        {
            Log?.LogWarning($"[TST-TRAIT-WRITE] ADD failed {key}: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static bool RemoveCharacterTrait(int i,string key)
    {
        var reader=At(affecterReaders,i);
        var comp=reader==null || reader is MissingComponent ? At(affecters,i) : GetMember(reader,"Ref");
        if(comp==null || comp is MissingComponent || string.IsNullOrWhiteSpace(key)) return false;

        try
        {
            Log?.LogInfo($"[TST-TRAIT-WRITE] REMOVE request char={i} key={key}");
            bool removed=false;

            // AffecterComponent exposes AddAffecter but no matching public RemoveAffecter.
            // Remove the holder from its source dictionary, then purge the component's cached
            // category/stat/type/positive lists before reloading the reader.
            var dict=GetMember(comp,"_affecters");
            if(RemoveDictionaryKey(dict,key))
            {
                removed=true;
                Log?.LogInfo("[TST-TRAIT-WRITE] REMOVE from _affecters");
            }

            foreach(var cacheName in new[]{"_affecterByCategories","_affecterByStats","_affectersByPositive","_affectersByType"})
            {
                var cache=GetMember(comp,cacheName);
                int n=RemoveStringFromDictionaryLists(cache,key);
                if(n>0) Log?.LogInfo($"[TST-TRAIT-WRITE] purged {n} cache refs from {cacheName}");
            }

            if(!removed)
            {
                Log?.LogWarning($"[TST-TRAIT-WRITE] key {key} not removed from _affecters");
                return false;
            }

            try { reader?.GetType().GetMethod("Reload",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(reader,null); } catch{}

            bool gone=!ReaderHasTrait(reader,key);
            Log?.LogInfo($"[TST-TRAIT-WRITE] REMOVE result key={key} gone={gone}");
            return gone;
        }
        catch(Exception ex)
        {
            Log?.LogWarning($"[TST-TRAIT-WRITE] REMOVE failed {key}: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static bool RemoveDictionaryKey(object? dict,string key)
    {
        if(dict==null) return false;
        try
        {
            var rm=dict.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .FirstOrDefault(m=>m.Name=="Remove" && m.GetParameters().Length==1 && m.GetParameters()[0].ParameterType==typeof(string));
            if(rm==null) return false;
            var r=rm.Invoke(dict,new object[]{key});
            return r is bool b ? b : true;
        }catch{return false;}
    }

    private static int RemoveStringFromDictionaryLists(object? dict,string key)
    {
        if(dict==null) return 0;
        int removed=0;
        try
        {
            foreach(var ko in EnumerateAny(GetMember(dict,"Keys")).ToList())
            {
                if(ko==null) continue;
                var list=GetDictionaryValue(dict,ko);
                if(list==null) continue;

                while(true)
                {
                    var rm=list.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                        .FirstOrDefault(m=>m.Name=="Remove" && m.GetParameters().Length==1 && m.GetParameters()[0].ParameterType==typeof(string));
                    if(rm==null) break;
                    var r=rm.Invoke(list,new object[]{key});
                    bool did=r is bool b ? b : false;
                    if(!did) break;
                    removed++;
                }
            }
        }catch{}
        return removed;
    }

    private static bool ReplaceCharacterTrait(int i,string oldKey,string newKey)
    {
        Log?.LogInfo($"[TST-TRAIT-WRITE] REPLACE {oldKey} -> {newKey}");
        if(string.Equals(oldKey,newKey,StringComparison.OrdinalIgnoreCase)) return true;
        if(!string.IsNullOrWhiteSpace(oldKey) && !RemoveCharacterTrait(i,oldKey)) return false;
        if(AddCharacterTrait(i,newKey)) return true;
        if(!string.IsNullOrWhiteSpace(oldKey)) AddCharacterTrait(i,oldKey);
        return false;
    }

    private static bool ReaderHasTrait(object? reader,string key)
    {
        if(reader==null || reader is MissingComponent) return false;
        try
        {
            var has=reader.GetType().GetMethod("Has",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
                null,new[]{typeof(string)},null);
            if(has!=null) return Convert.ToBoolean(has.Invoke(reader,new object[]{key}) ?? false);
        }catch{}
        return GetAllAffecterKeysFromReader(reader).Contains(key,StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> GetAllAffecterKeysFromReader(object reader)
    {
        var result=new List<string>();
        try
        {
            var m=reader.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .FirstOrDefault(x=>x.Name=="GetAll" && x.GetParameters().Length==0);
            foreach(var x in EnumerateAny(m?.Invoke(reader,null)))
            {
                var s=x?.ToString()??"";
                if(!string.IsNullOrWhiteSpace(s)) result.Add(s);
            }
        }catch{}
        return result;
    }

    private static object? DefaultFor(Type t)
    {
        if(!t.IsValueType) return null;
        try { return Activator.CreateInstance(t); } catch { return null; }
    }

    private sealed class ActiveTraitGroups
    {
        public List<string> Main=new();
        public List<string> Sub=new();
    }

    private static ActiveTraitGroups GetActiveTraitGroupsByType(int i,string raceKey)
    {
        var result=new ActiveTraitGroups();
        var reader=At(affecterReaders,i);
        if(reader==null || reader is MissingComponent) return result;

        try
        {
            var typeEnum=GameTypes().FirstOrDefault(t=>t.FullName=="Refactor.AffecterType" || (t.Name=="AffecterType" && t.IsEnum));
            var get=reader.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .FirstOrDefault(m=>m.Name=="GetAllByType" && m.GetParameters().Length==1);
            if(typeEnum!=null && get!=null)
            {
                foreach(var name in Enum.GetNames(typeEnum))
                {
                    object ev; try{ev=Enum.Parse(typeEnum,name);}catch{continue;}
                    object? source=null; try{source=get.Invoke(reader,new[]{ev});}catch{continue;}
                    var keys=EnumerateAny(source).Select(x=>x?.ToString()??"")
                        .Where(k=>IsSelectableRecruitTrait(k) && !string.Equals(k,raceKey,StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if(keys.Count==0) continue;

                    var low=name.ToLowerInvariant();
                    if(low.Contains("race")) continue;
                    if(low.Contains("background") || low.Contains("main"))
                        result.Main.AddRange(keys);
                    else if(low.Contains("individual") || low.Contains("sub") || low.Contains("trait"))
                        result.Sub.AddRange(keys);
                }
            }
        }catch{}

        result.Main=result.Main.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.Sub=result.Sub.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Conservative fallback: only classify active affecters that are already in trusted catalogs.
        // This prevents game options/status affecters from ever appearing as traits.
        var observed=GetAllAffecterKeys(i)
            .Where(k=>IsSelectableRecruitTrait(k) && !string.Equals(k,raceKey,StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach(var k in observed)
        {
            if(mainTraitCatalog.Contains(k,StringComparer.OrdinalIgnoreCase) &&
               !result.Main.Contains(k,StringComparer.OrdinalIgnoreCase))
                result.Main.Add(k);
            else if(subTraitCatalog.Contains(k,StringComparer.OrdinalIgnoreCase) &&
                    !result.Sub.Contains(k,StringComparer.OrdinalIgnoreCase))
                result.Sub.Add(k);
        }

        return result;
    }

    private static List<string> GetAllAffecterKeys(int i)
    {
        var reader=At(affecterReaders,i);
        if(reader==null || reader is MissingComponent) return new List<string>();
        return GetAllAffecterKeysFromReader(reader).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly HashSet<string> dumpedTraitDataTypes=new();
    private static bool dumpedTraitApiScan;
    private static void DumpUnitTraitData(object unit)
    {
        var data=GetMember(unit,"Data");
        if(data!=null)
        {
            var t=data.GetType();
            if(dumpedTraitDataTypes.Add(t.FullName??t.Name))
            {
                Log?.LogInfo($"[TST-TRAITDATA] ==== {t.FullName} ====");
                try
                {
                    foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                    {
                        var n=p.Name.ToLowerInvariant();
                        if(n.Contains("trait")||n.Contains("race")||n.Contains("class")||n.Contains("skill")||n.Contains("background"))
                        {
                            object? v=null; try{v=p.GetValue(data);}catch{}
                            Log?.LogInfo($"[TST-TRAITDATA] PROP {p.Name}:{p.PropertyType.FullName}={v}");
                        }
                    }
                    foreach(var fld in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                    {
                        var n=fld.Name.ToLowerInvariant();
                        if(n.Contains("trait")||n.Contains("race")||n.Contains("class")||n.Contains("skill")||n.Contains("background"))
                        {
                            object? v=null; try{v=fld.GetValue(data);}catch{}
                            Log?.LogInfo($"[TST-TRAITDATA] FIELD {fld.Name}:{fld.FieldType.FullName}={v}");
                        }
                    }
                }catch{}
            }
        }

        if(dumpedTraitApiScan) return;
        dumpedTraitApiScan=true;
        foreach(var t in GameTypes())
        {
            var tn=(t.FullName??t.Name).ToLowerInvariant();
            if(!tn.Contains("trait")) continue;
            MethodInfo[] ms; try{ms=t.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);}catch{continue;}
            foreach(var m in ms)
            {
                var n=m.Name.ToLowerInvariant();
                if(n.Contains("trait")||n.Contains("candidate")||n.Contains("background")||n.Contains("race"))
                    Log?.LogInfo($"[TST-TRAITAPI] {t.FullName}::{m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))})->{m.ReturnType.FullName} static={m.IsStatic}");
            }
        }
    }

    private static Dictionary<string,List<string>> GetTraitGroups(int i)
    {
        var result=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        var reader=At(affecterReaders,i);
        if(reader==null || reader is MissingComponent) return result;

        var catType=GameTypes().FirstOrDefault(t=>t.Name=="AffecterCategory" && t.IsEnum);
        if(catType==null) return result;

        var get=reader.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .FirstOrDefault(m=>m.Name=="GetAffectersBy" && m.GetParameters().Length==1 && m.GetParameters()[0].ParameterType.Name=="AffecterCategory");
        if(get==null) return result;

        foreach(var name in Enum.GetNames(catType))
        {
            var low=name.ToLowerInvariant();
            if(!(low.Contains("trait")||low.Contains("race")||low.Contains("background")||low.Contains("class"))) continue;
            try
            {
                var ev=Enum.Parse(catType,name);
                var source=get.Invoke(reader,new[]{ev});
                var keys=EnumerateAny(source).Select(x=>x?.ToString()??"").Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                if(keys.Count>0) result[name]=keys;
            }catch{}
        }
        return result;
    }

    private static List<string> GetSkillKeys(int i)
    {
        var result=new List<string>();
        var reader=At(skillReaders,i);
        if(reader==null || reader is MissingComponent) return result;
        try
        {
            var m=reader.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .FirstOrDefault(x=>x.Name=="GetAllSkills" && x.GetParameters().Length==0);
            var source=m?.Invoke(reader,null);
            result=EnumerateAny(source).Select(x=>x?.ToString()??"").Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        }catch{}
        return result;
    }

    private static string FriendlyBuildKey(string key)
    {
        var s=key.Replace("AFFECTER_","").Replace("SKILL_","").Replace("TRAIT_","").Replace("_"," ");
        return FriendlyItemName(s);
    }

    private static void DrawGame(float x, float y)
    {
        SectionTitle(x + 36, y + 160, 620, "SETTLEMENT TREASURY");

        Label(x + 56, y + 208, 180, 28, "Clan Gold");
        goldText = TextField(x + 215, y + 204, 170, 32, goldText);
        SetBackground(0.31f,0.20f,0.09f);
        if (Button(x + 402, y + 204, 80, 32, "SET") && int.TryParse(goldText, out var g)) SetGold(g);
        Label(x + 56, y + 244, Math.Max(700,windowW-120), 38, "Gold changes immediately. The game's HUD will show the new amount after gold is earned or spent.");

        SectionTitle(x + 36, y + 286, 620, "TIME CONTROL");
        Label(x + 56, y + 338, 180, 28, "Time Scale");
        SetBackground(0.31f,0.20f,0.09f);
        if (Button(x + 215, y + 332, 86, 34, "0.5x")) SetGameSpeed(0.5f);
        if (Button(x + 312, y + 332, 86, 34, "1x")) SetGameSpeed(1f);
        if (Button(x + 409, y + 332, 86, 34, "2x")) SetGameSpeed(2f);
        if (Button(x + 506, y + 332, 86, 34, "5x")) SetGameSpeed(5f);

        SectionTitle(x + 36, y + 410, 620, "TREASURE TOOL");
        SetBackground(0.16f,0.12f,0.075f);
        Box(x + 52, y + 454, 600, 90, "");
        Label(x + 72, y + 470, 530, 22, "F8 toggles the menu. Drag only the top banner.");
        Label(x + 72, y + 496, 530, 22, "Clicks behind this window are blocked; clicks outside it pass to the game.");
        Label(x + 72, y + 522, 530, 22, "Trait discoveries are learned locally and carried into future saves.");
    }

    private static void FillRect(float x,float y,float w,float h,float r,float g,float b,float a=1f)
    {
        if(guiDrawTexture==null || whiteTextureProp==null || colorType==null) return;
        try
        {
            var old=guiColorProp?.GetValue(null);
            var col=Activator.CreateInstance(colorType,new object[]{r,g,b,a});
            guiColorProp?.SetValue(null,col);
            var tex=whiteTextureProp.GetValue(null);
            if(tex!=null) guiDrawTexture.Invoke(null,new[]{Rect(x,y,w,h),tex});
            if(old!=null) guiColorProp?.SetValue(null,old);
        }
        catch{}
    }

    private static void SetBackground(float r,float g,float b,float a=1f)
    {
        if(colorType==null) return;
        try {
            var col=Activator.CreateInstance(colorType,new object[]{r,g,b,a});
            guiBackgroundColorProp?.SetValue(null,col);
        } catch {}
    }

    private static void SectionTitle(float x,float y,float w,string text)
    {
        FillRect(x,y,w,34,0.75f,0.73f,0.70f,1f);
        FillRect(x,y,w-1,1,0.5f,0.5f,0.5f,1f); // Top dark
        FillRect(x,y,1,33,0.5f,0.5f,0.5f,1f); // Left dark
        FillRect(x,y+33,w,1,1f,1f,1f,1f); // Bottom light
        FillRect(x+w-1,y,1,34,1f,1f,1f,1f); // Right light
        SetBackground(0.75f,0.73f,0.70f);
        Box(x,y,w,34,"");
        Label(x+14,y+7,w-28,24,text);
    }

    private static string FriendlyItemName(string name)
    {
        var s=name.Replace("ITEM_","").Replace("_"," ");
        var chars=new List<char>();
        for(int i=0;i<s.Length;i++){
            if(i>0 && char.IsUpper(s[i]) && char.IsLower(s[i-1])) chars.Add(' ');
            chars.Add(s[i]);
        }
        return new string(chars.ToArray()).Trim();
    }

    private static void StyleSkin()
    {
        if(skinStyled || guiSkinProp==null) return;
        try {
            var skin=guiSkinProp.GetValue(null); if(skin==null) return;
            foreach(var name in new[]{"label","button","box","textField","toggle"})
            {
                var p=skin.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance);
                var style=p?.GetValue(skin); if(style==null) continue;
                var fs=style.GetType().GetProperty("fontSize",BindingFlags.Public|BindingFlags.Instance);
                if(fs?.CanWrite==true) fs.SetValue(style,name=="label" ? 14 : 13);
                var rw=style.GetType().GetProperty("wordWrap",BindingFlags.Public|BindingFlags.Instance);
                if(rw?.CanWrite==true && name=="label") rw.SetValue(style,true);
            }
            skinStyled=true;
        } catch {}
    }

    private static void ConsumeMouseInput()
    {
        var ev=eventCurrent?.GetValue(null); if(ev==null) return;
        var kind=eventTypeProp?.GetValue(ev)?.ToString() ?? "";
        if(!(kind.StartsWith("Mouse") || kind=="ScrollWheel")) return;
        var mp=eventMouseProp?.GetValue(ev); if(mp==null) return;
        float mx=Convert.ToSingle(GetMember(mp,"x") ?? -1f);
        float my=Convert.ToSingle(GetMember(mp,"y") ?? -1f);
        float w=windowW,h=windowH;
        if(mx>=windowX && mx<=windowX+w && my>=windowY && my<=windowY+h)
            try { eventUseMethod?.Invoke(ev,null); } catch {}
    }

    private static void CenterWindow(float w,float h)
    {
        try {
            var wp=screenType?.GetProperty("width",BindingFlags.Public|BindingFlags.Static);
            var hp=screenType?.GetProperty("height",BindingFlags.Public|BindingFlags.Static);
            float sw=Convert.ToSingle(wp?.GetValue(null) ?? 1280);
            float sh=Convert.ToSingle(hp?.GetValue(null) ?? 720);
            windowX=Math.Max(10,(sw-w)/2f); windowY=Math.Max(10,(sh-h)/2f);
        } catch { windowX=120; windowY=80; }
    }

    private static void HideHardwareCursorIfHovering()
    {
        var ev=eventCurrent?.GetValue(null); if(ev==null) return;
        var mp=eventMouseProp?.GetValue(ev); if(mp==null) return;
        float mx=Convert.ToSingle(GetMember(mp,"x") ?? -1f);
        float my=Convert.ToSingle(GetMember(mp,"y") ?? -1f);
        if(mx>=windowX && mx<=windowX+windowW && my>=windowY && my<=windowY+windowH)
        {
            try { cursorVisibleProp?.SetValue(null, false); } catch {}
        }
    }

    private static void HandleWindowDragAndResize()
    {
        var ev=eventCurrent?.GetValue(null); if(ev==null) return;
        var kind=eventTypeProp?.GetValue(ev)?.ToString();
        var mp=eventMouseProp?.GetValue(ev); if(mp==null) return;
        float mx=Convert.ToSingle(GetMember(mp,"x") ?? 0f);
        float my=Convert.ToSingle(GetMember(mp,"y") ?? 0f);
        int button=0; try{button=Convert.ToInt32(eventButtonProp?.GetValue(ev) ?? 0);}catch{}

        if(kind=="MouseDown" && button==0)
        {
            bool onResize=mx>=windowX+windowW-34 && mx<=windowX+windowW &&
                          my>=windowY+windowH-34 && my<=windowY+windowH;
            if(onResize)
            {
                resizing=true; dragging=false;
                resizeStartMouseX=mx; resizeStartMouseY=my;
                resizeStartW=windowW; resizeStartH=windowH;
            }
            else if(mx>=windowX && mx<=windowX+windowW && my>=windowY && my<=windowY+62)
            {
                dragging=true; resizing=false;
                dragOffsetX=mx-windowX; dragOffsetY=my-windowY;
            }
            else { dragging=false; resizing=false; }
        }
        else if(kind=="MouseDrag" && resizing)
        {
            float sw=Convert.ToSingle(screenType?.GetProperty("width",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) ?? 1920);
            float sh=Convert.ToSingle(screenType?.GetProperty("height",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) ?? 1080);
            windowW=Math.Max(MinWindowW,Math.Min(sw-windowX-8,resizeStartW+(mx-resizeStartMouseX)));
            windowH=Math.Max(MinWindowH,Math.Min(sh-windowY-8,resizeStartH+(my-resizeStartMouseY)));
        }
        else if(kind=="MouseDrag" && dragging)
        {
            windowX=Math.Max(0,mx-dragOffsetX); windowY=Math.Max(0,my-dragOffsetY);
        }
        else if(kind=="MouseUp")
        {
            dragging=false; resizing=false;
        }
    }

    private static void ApplyPalette()
    {
        if(colorType==null) return;
        try {
            object C(float r,float g,float b,float a=1f)=>Activator.CreateInstance(colorType!,new object[]{r,g,b,a})!;
            guiColorProp?.SetValue(null,C(1f,1f,1f,1f)); // White overall tint so elements are visible
            guiContentColorProp?.SetValue(null,C(0f,0f,0f,1f)); // Classic black text color
            guiBackgroundColorProp?.SetValue(null,C(0.83f,0.81f,0.78f,1f)); // Classic gray
        } catch {}
    }

    private static void DiscoverRuntimeObjects(bool force)
    {
        // Never poll the scene while the menu is open. The game is hooked for player/clan changes.
        // A full discovery pass is only allowed on an explicit menu-open refresh.
        if(!force) return;

        ResolveClan();
        ResolveEntityComponentSystem();
        ResolvePlayerUnits();
        ResolveTraitSheetFromDataSheetManager();
        traitCatalogAttempted=false;

        Log?.LogDebug($"Discovery(open): players={playerUnits.Count}, statuses={statuses.Count}, inventories={inventories.Count}, clan={(clan!=null)}, traitSheet={(cachedTraitSheet!=null)}");
    }

    private static void ResolveEntityComponentSystem()
    {
        if(entityComponentSystem!=null) return;
        var target=GameTypes().FirstOrDefault(t=>t.FullName=="Refactor.Main.EntityComponent" || t.Name=="EntityComponent");
        if(target==null) return;

        foreach(var ownerType in GameTypes())
        {
            foreach(var obj in FindObjectsOfType(ownerType))
            {
                try
                {
                    foreach(var p in obj.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                    {
                        object? v=null; try{v=p.GetValue(obj);}catch{}
                        if(v!=null && target.IsInstanceOfType(v)) { entityComponentSystem=v; Log?.LogInfo("[TST-ECS] Found EntityComponent via property"); return; }
                    }
                    foreach(var fld in obj.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                    {
                        object? v=null; try{v=fld.GetValue(obj);}catch{}
                        if(v!=null && target.IsInstanceOfType(v)) { entityComponentSystem=v; Log?.LogInfo("[TST-ECS] Found EntityComponent via field"); return; }
                    }
                }
                catch{}
            }
        }
    }

    private static void ResolveSettlementItemContainer()
    {
        if(settlementItemContainer!=null) return;
        foreach(var t in GameTypes())
        {
            foreach(var obj in FindObjectsOfType(t))
            {
                try
                {
                    foreach(var p in obj.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                    {
                        var pt=p.PropertyType.FullName??"";
                        if(!pt.Contains("ItemEntity")) continue;
                        object? v=null; try{v=p.GetValue(obj);}catch{}
                        if(v==null) continue;
                        settlementItemContainer=v;
                        DumpSettlementItemContainerShape(v);
                        dumpedItemContainerShape=true;
                        return;
                    }
                } catch{}
            }
        }
    }

    private static void ResolveClan()
    {
        if(clan!=null) return;

        foreach(var t in GameTypes())
        {
            try
            {
                if(!t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Any(m=>m.Name=="AddClanGold"))
                    continue;

                var found=FindObjectsOfType(t);
                foreach(var obj in found)
                {
                    clan=obj;
                    return;
                }
            }
            catch{}
        }
    }

    private static void ResolvePlayerUnits()
    {
        // First, try any concrete runtime owner exposing GetAllPlayerUnits.
        foreach(var t in GameTypes())
        {
            MethodInfo[] candidates;
            try {
                candidates=t.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
                    .Where(m=>m.Name=="GetAllPlayerUnits").ToArray();
            } catch { continue; }

            foreach(var getAll in candidates)
            {
                try
                {
                    object? provider=null;
                    if(!getAll.IsStatic)
                    {
                        provider=FindObjectsOfType(t).FirstOrDefault();
                        if(provider==null) continue;
                    }

                    object?[] args=getAll.GetParameters().Select(p => p.ParameterType==typeof(bool) ? (object)true : (p.HasDefaultValue ? p.DefaultValue : Activator.CreateInstance(p.ParameterType)!)).ToArray();
                    if(getAll.Invoke(provider,args) is not IEnumerable result) continue;

                    bool changed=false;
                    foreach(var u in result)
                    {
                        if(u==null || !u.GetType().Name.Contains("UnitEntity")) continue;
                        if(!playerUnits.Contains(u)) { playerUnits.Add(u); changed=true; }
                    }
                    if(changed)
                    {
                        playerUnitProvider=provider;
                        RebuildPlayerCaches();
                    }
                }
                catch{}
            }
        }
    }

    private static void RebuildPlayerCaches()
    {
        statuses.Clear();
        inventories.Clear();
        statusReaders.Clear();
        inventoryReaders.Clear();
        affecterReaders.Clear();
        affecters.Clear();
        skillReaders.Clear();
        skills.Clear();
        playerNames.Clear();

        for(int i=0;i<playerUnits.Count;i++)
        {
            var unit=playerUnits[i];
            playerNames.Add(ResolvePlayerName(unit,i));
            var profileObj=GetMember(unit,"_profile");
            if(profileObj!=null) DumpInterestingObject("[TST-PROFILE]",profileObj);

            var statusReader=ResolveReader(unit,"StatusReader");
            var inventoryReader=ResolveReader(unit,"InventoryReader");
            var affecterReader=ResolveReader(unit,"AffecterReader");
            var skillReader=ResolveReader(unit,"SkillReader");

            // The game readers directly expose their backing mutable components through Ref.
            // This is more reliable than trying to rediscover the central ECS container.
            var status=statusReader==null ? null : GetMember(statusReader,"Ref");
            var inventory=inventoryReader==null ? null : GetMember(inventoryReader,"Ref");
            var affecter=affecterReader==null ? null : GetMember(affecterReader,"Ref");
            var skill=skillReader==null ? null : GetMember(skillReader,"Ref");

            // Fallback if the primary component reference is unavailable. changes the reader layout.
            status ??= ResolveMutableComponent(unit,"StatusComponent");
            inventory ??= ResolveMutableComponent(unit,"InventoryComponent");

            if(status!=null) DumpComponentShape(status);
            if(inventory!=null) DumpComponentShape(inventory);

            statusReaders.Add(statusReader ?? new MissingComponent());
            inventoryReaders.Add(inventoryReader ?? new MissingComponent());
            affecterReaders.Add(affecterReader ?? new MissingComponent());
            skillReaders.Add(skillReader ?? new MissingComponent());
            statuses.Add(status ?? new MissingComponent());
            inventories.Add(inventory ?? new MissingComponent());
            affecters.Add(affecter ?? new MissingComponent());
            skills.Add(skill ?? new MissingComponent());

            Log?.LogInfo($"[TST-ECS] {playerNames[^1]} readerStatus={(statusReader==null?"MISS":statusReader.GetType().FullName)} readerInv={(inventoryReader==null?"MISS":inventoryReader.GetType().FullName)} status={(status==null?"MISS":status.GetType().FullName)} inventory={(inventory==null?"MISS":inventory.GetType().FullName)}");
        }

        if(selectedCharacter>=playerUnits.Count) selectedCharacter=Math.Max(0,playerUnits.Count-1);
        loadedCharacter=-1;
    }

    private static object? ResolveReader(object unit,string readerTypeName)
    {
        var readerType=GameTypes().FirstOrDefault(t=>t.Name==readerTypeName);
        if(readerType==null) return null;

        var iEntityType=GameTypes().FirstOrDefault(t=>t.FullName=="Refactor.IEntity" || t.Name=="IEntity");
        object entityArg=unit;
        if(iEntityType!=null && !iEntityType.IsInstanceOfType(unit))
        {
            var casted=CastIl2CppObject(unit,iEntityType);
            if(casted!=null) entityArg=casted;
        }

        foreach(var ext in GameTypes().Where(t=>t.FullName=="Refactor.EntityExtensions" || t.Name=="EntityExtensions"))
        {
            foreach(var m in ext.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))
            {
                try
                {
                    if(m.Name!="GetComponent" || !m.IsGenericMethodDefinition || m.GetGenericArguments().Length!=1) continue;
                    var ps=m.GetParameters();
                    if(ps.Length!=1) continue;
                    var pn=ps[0].ParameterType.FullName ?? ps[0].ParameterType.Name;
                    if(!pn.Contains("IEntity",StringComparison.OrdinalIgnoreCase)) continue;

                    var v=m.MakeGenericMethod(readerType).Invoke(null,new[]{entityArg});
                    if(v!=null)
                    {
                        Log?.LogInfo($"[TST-READER] {readerTypeName} bound");
                        DumpReaderShape(v,readerTypeName);
                        return v;
                    }
                }
                catch(Exception e)
                {
                    Log?.LogDebug($"[TST-READER] {readerTypeName} failed: {e.GetBaseException().Message}");
                }
            }
        }
        return null;
    }

    private static object? ResolveMutableComponent(object unit,string componentTypeName)
    {
        if(entityComponentSystem==null) return null;

        var componentType=GameTypes().FirstOrDefault(t=>t.Name==componentTypeName);
        if(componentType==null) return null;

        var iEntityType=GameTypes().FirstOrDefault(t=>t.FullName=="Refactor.IEntity" || t.Name=="IEntity");
        object entityArg=unit;
        if(iEntityType!=null && !iEntityType.IsInstanceOfType(unit))
        {
            var casted=CastIl2CppObject(unit,iEntityType);
            if(casted!=null) entityArg=casted;
        }

        foreach(var m in entityComponentSystem.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
        {
            try
            {
                if(m.Name!="GetComponentOf" || !m.IsGenericMethodDefinition || m.GetGenericArguments().Length!=1) continue;
                var ps=m.GetParameters();
                if(ps.Length!=1) continue;

                var v=m.MakeGenericMethod(componentType).Invoke(entityComponentSystem,new[]{entityArg});
                if(v!=null)
                {
                    Log?.LogInfo($"[TST-COMP] {componentTypeName} bound through EntityComponent.GetComponentOf<T>");
                    return v;
                }
            }
            catch(Exception e)
            {
                Log?.LogDebug($"[TST-COMP] {componentTypeName} failed: {e.GetBaseException().Message}");
            }
        }
        return null;
    }

    private static string ResolvePlayerName(object unit,int index)
    {
        foreach(var t in GameTypes())
        {
            try
            {
                var m=t.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
                    .FirstOrDefault(x=>x.Name=="GetPlayerUnitDisplayName" && x.GetParameters().Length==1 &&
                        x.GetParameters()[0].ParameterType.IsAssignableFrom(unit.GetType()));
                if(m!=null)
                {
                    var v=m.Invoke(null,new[]{unit})?.ToString();
                    if(!string.IsNullOrWhiteSpace(v)) return CleanName(v);
                }
            }
            catch{}
        }


        foreach(var name in new[]{"GetPlayerUnitDisplayName","GetUnitName","GetDisplayName"})
        {
            try
            {
                var m=unit.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .FirstOrDefault(x=>x.Name==name && x.GetParameters().Length==0);
                if(m!=null)
                {
                    var v=m.Invoke(unit,null)?.ToString();
                    if(!string.IsNullOrWhiteSpace(v)) return CleanName(v);
                }
            }
            catch{}
        }

        foreach(var member in new[]{"DisplayName","UnitName","Name","UnitNameText","UnitNameTextKey","DefaultUnitNameTextKey"})
        {
            var v=GetMember(unit,member)?.ToString();
            if(!string.IsNullOrWhiteSpace(v) && !v.StartsWith("UNIT_",StringComparison.OrdinalIgnoreCase))
                return CleanName(v);
        }

        if(playerUnitProvider!=null)
        {
            foreach(var name in new[]{"GetPlayerUnitDisplayName","GetUnitName"})
            {
                try
                {
                    foreach(var m in playerUnitProvider.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(x=>x.Name==name && x.GetParameters().Length==1))
                    {
                        var p=m.GetParameters()[0].ParameterType;
                        object arg = p.IsInstanceOfType(unit) ? unit : Convert.ChangeType(index,p);
                        var v=m.Invoke(playerUnitProvider,new[]{arg})?.ToString();
                        if(!string.IsNullOrWhiteSpace(v)) return CleanName(v);
                    }
                }
                catch{}
            }
        }

        return $"Player {index+1}";
    }

    private static string CleanName(string s)
    {
        if(s.StartsWith("System.String",StringComparison.OrdinalIgnoreCase)) return s;
        return s.Replace("_"," ").Trim();
    }

    public static string GetCharacterName(int i)
    {
        if(i>=0 && i<playerNames.Count) return playerNames[i];
        return $"Player {i+1}";
    }

    private static object? ResolveComponent(object unit,string desiredTypeName)
    {
        var targetType=GameTypes().FirstOrDefault(t=>t.Name==desiredTypeName);
        if(targetType==null) return null;

        // 0) Dungeon Settlers' own Refactor.EntityExtensions is the authoritative component bridge.
        // UnitEntity is an IL2CPP proxy. Reflection can see that GetComponent<T> wants Refactor.IEntity,
        // but normal managed assignability may fail, so explicitly cast the proxy to IEntity first.
        var iEntityType=GameTypes().FirstOrDefault(t=>t.FullName=="Refactor.IEntity" || t.Name=="IEntity");
        object entityArg=unit;
        if(iEntityType!=null && !iEntityType.IsInstanceOfType(unit))
        {
            var casted=CastIl2CppObject(unit,iEntityType);
            if(casted!=null) entityArg=casted;
        }

        foreach(var ext in GameTypes().Where(t=>t.FullName=="Refactor.EntityExtensions" || t.Name=="EntityExtensions"))
        {
            MethodInfo[] methods;
            try { methods=ext.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic); }
            catch { continue; }

            foreach(var m in methods)
            {
                try
                {
                    if(m.Name!="GetComponent") continue;
                    if(!m.IsGenericMethodDefinition || m.GetGenericArguments().Length!=1) continue;

                    var ps=m.GetParameters();
                    if(ps.Length!=1) continue;
                    var pn=ps[0].ParameterType.FullName ?? ps[0].ParameterType.Name;
                    if(!pn.Contains("IEntity",StringComparison.OrdinalIgnoreCase)) continue;

                    var closed=m.MakeGenericMethod(targetType);
                    var v=closed.Invoke(null,new[]{entityArg});
                    if(v!=null)
                    {
                        Log?.LogInfo($"[TST-BIND] {desiredTypeName} via {ext.FullName}::GetComponent<T>(IEntity)");
                        return v;
                    }
                }
                catch(Exception e)
                {
                    Log?.LogInfo($"[TST-BIND-FAIL] GetComponent<{desiredTypeName}>: {e.GetBaseException().Message}");
                }
            }
        }

        // 1) Known direct members on UnitEntity.
        foreach(var member in new[]{desiredTypeName,desiredTypeName.Replace("Component",""),"_info","_profile","Info","Profile","EntityComponent","ComponentList","Components","ComponentContainer"})
        {
            var direct=GetMember(unit,member);
            var hit=FindComponentInValue(direct,desiredTypeName);
            if(hit!=null) return hit;
            hit=TryResolveFromProvider(direct,targetType,desiredTypeName);
            if(hit!=null) return hit;
        }

        // 2) Generic instance GetComponent<T>().
        foreach(var m in unit.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
        {
            try
            {
                if(!m.IsGenericMethodDefinition || m.GetGenericArguments().Length!=1 || m.GetParameters().Length!=0) continue;
                if(!(m.Name=="GetComponent" || m.Name=="GetComponentOf")) continue;
                var v=m.MakeGenericMethod(targetType).Invoke(unit,null);
                if(v!=null) return v;
            }
            catch{}
        }

        // 3) Static extension helpers: GetComponent<T>(IEntity) / GetComponentOf<T>(IEntity).
        foreach(var t in GameTypes())
        {
            MethodInfo[] methods;
            try { methods=t.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic); } catch { continue; }

            foreach(var m in methods)
            {
                try
                {
                    if(!(m.Name=="GetComponent" || m.Name=="GetComponentOf" || m.Name=="TryGetComponent")) continue;
                    var ps=m.GetParameters();

                    if(m.IsGenericMethodDefinition && m.GetGenericArguments().Length==1 && ps.Length==1 &&
                       ps[0].ParameterType.IsAssignableFrom(unit.GetType()))
                    {
                        var v=m.MakeGenericMethod(targetType).Invoke(null,new[]{unit});
                        if(v!=null) return v;
                    }

                    // Some IL2CPP helper methods are emitted non-generically and accept IEntity + ComponentType.
                    if(!m.IsGenericMethodDefinition && ps.Length>=2 && ps[0].ParameterType.IsAssignableFrom(unit.GetType()))
                    {
                        var enumParam=ps.FirstOrDefault(p=>p.ParameterType.IsEnum);
                        if(enumParam!=null)
                        {
                            var wanted=Enum.GetNames(enumParam.ParameterType)
                                .FirstOrDefault(n=>n.Contains(desiredTypeName.Replace("Component",""),StringComparison.OrdinalIgnoreCase));
                            if(wanted!=null)
                            {
                                var args=new object?[ps.Length];
                                args[0]=unit;
                                for(int ai=1;ai<ps.Length;ai++)
                                {
                                    var pt=ps[ai].ParameterType;
                                    if(pt==enumParam.ParameterType) args[ai]=Enum.Parse(pt,wanted);
                                    else if(pt.IsByRef) args[ai]=null;
                                    else if(ps[ai].HasDefaultValue) args[ai]=ps[ai].DefaultValue;
                                    else args[ai]=pt.IsValueType ? Activator.CreateInstance(pt) : null;
                                }
                                var ret=m.Invoke(null,args);
                                foreach(var a in args)
                                    if(a!=null && targetType.IsInstanceOfType(a)) return a;
                                if(ret!=null && targetType.IsInstanceOfType(ret)) return ret;
                            }
                        }
                    }
                }
                catch{}
            }
        }

        // 4) Search one level of UnitEntity members for a component provider.
        foreach(var p in unit.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
        {
            object? v=null; try{v=p.GetValue(unit);}catch{}
            var hit=FindComponentInValue(v,desiredTypeName);
            if(hit!=null) return hit;
            hit=TryResolveFromProvider(v,targetType,desiredTypeName);
            if(hit!=null) return hit;
        }

        foreach(var fld in unit.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
        {
            object? v=null; try{v=fld.GetValue(unit);}catch{}
            var hit=FindComponentInValue(v,desiredTypeName);
            if(hit!=null) return hit;
            hit=TryResolveFromProvider(v,targetType,desiredTypeName);
            if(hit!=null) return hit;
        }

        return null;
    }

    private static void DumpReaderShape(object reader,string label)
    {
        var key=reader.GetType().FullName ?? reader.GetType().Name;
        if(!dumpedReaderShapes.Add(key)) return;

        Log?.LogInfo($"[TST-SHAPE] ==== {label} {key} ====");
        try
        {
            foreach(var p in reader.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                string value="";
                try
                {
                    if(p.GetIndexParameters().Length==0)
                        value=DescribeRuntimeValue(p.GetValue(reader));
                }
                catch(Exception e){ value=$"<ERR {e.GetBaseException().Message}>"; }
                Log?.LogInfo($"[TST-SHAPE] PROP {p.Name} : {p.PropertyType.FullName} = {value}");
            }

            foreach(var fld in reader.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                string value="";
                try { value=DescribeRuntimeValue(fld.GetValue(reader)); }
                catch(Exception e){ value=$"<ERR {e.GetBaseException().Message}>"; }
                Log?.LogInfo($"[TST-SHAPE] FIELD {fld.Name} : {fld.FieldType.FullName} = {value}");
            }

            foreach(var m in reader.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                if(m.IsSpecialName) continue;
                var sig=string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName+" "+p.Name));
                Log?.LogInfo($"[TST-SHAPE] METHOD {m.Name}({sig}) -> {m.ReturnType.FullName}");
            }
        }
        catch(Exception e){ Log?.LogWarning($"[TST-SHAPE] dump failed: {e.GetBaseException().Message}"); }
    }

    private static string DescribeRuntimeValue(object? v)
    {
        if(v==null) return "null";
        try
        {
            var t=v.GetType();
            if(t.IsPrimitive || v is string || v is decimal || t.IsEnum) return v.ToString() ?? "";
            if(v is IEnumerable e && v is not string)
            {
                int n=0;
                var samples=new List<string>();
                foreach(var x in e)
                {
                    if(x!=null && samples.Count<3) samples.Add(x.GetType().FullName ?? x.GetType().Name);
                    if(++n>=64) break;
                }
                return $"Enumerable count~{n} samples=[{string.Join(",",samples)}]";
            }
            return t.FullName ?? t.Name;
        }
        catch { return "<unreadable>"; }
    }

    private static object? InvokeReaderStat(object reader,string name)
    {
        var statType=GameTypes().FirstOrDefault(t=>t.Name=="StatType" && t.IsEnum);
        if(statType==null) return null;

        string[] aliases=name switch
        {
            "HealthBody" => new[]{"HealthBody","BodyHealth","Health"},
            "HealthHead" => new[]{"HealthHead","HeadHealth"},
            "MainSkillPoint" => new[]{"MainSkillPoint","MainSkillPoints"},
            "SubSkillPoint" => new[]{"SubSkillPoint","SubSkillPoints"},
            "Exp" => new[]{"Exp","Experience"},
            _ => new[]{name}
        };

        object? enumValue=null;
        foreach(var alias in aliases)
        {
            var enumName=Enum.GetNames(statType).FirstOrDefault(n=>n.Equals(alias,StringComparison.OrdinalIgnoreCase));
            if(enumName!=null) { enumValue=Enum.Parse(statType,enumName); break; }
        }
        if(enumValue==null) return null;

        foreach(var methodName in new[]{"GetValue","GetDefaultValue","GetStatus"})
        {
            try
            {
                var m=reader.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .FirstOrDefault(x=>x.Name==methodName && x.GetParameters().Length==1 &&
                        x.GetParameters()[0].ParameterType.Name=="StatType");
                if(m==null) continue;
                var v=m.Invoke(reader,new[]{enumValue});
                if(v==null) continue;
                if(methodName=="GetStatus")
                {
                    var inner=GetMember(v,"Value") ?? GetMember(v,"CurrentValue");
                    if(inner!=null) return inner;
                    var gm=v.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                        .FirstOrDefault(x=>x.Name=="GetValue" && x.GetParameters().Length==0);
                    if(gm!=null) return gm.Invoke(v,null);
                }
                return v;
            }
            catch(Exception e){ Log?.LogDebug($"[TST-STAT] {methodName}({name}) failed: {e.GetBaseException().Message}"); }
        }
        return null;
    }

    private static object? ProbeReaderValue(object reader,string name)
    {
        var stat=InvokeReaderStat(reader,name);
        if(stat!=null) return stat;
        var candidates=new[]{name,"Status_"+name,"get_"+name,"Get"+name,"GetStatus"+name};
        foreach(var cand in candidates)
        {
            try
            {
                var p=reader.GetType().GetProperty(cand,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.IgnoreCase);
                if(p!=null && p.GetIndexParameters().Length==0) return p.GetValue(reader);
            } catch {}
            try
            {
                var m=reader.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .FirstOrDefault(x=>x.Name.Equals(cand,StringComparison.OrdinalIgnoreCase) && x.GetParameters().Length==0);
                if(m!=null) return m.Invoke(reader,null);
            } catch {}
        }
        return null;
    }

    private static readonly HashSet<string> dumpedBuildTypes=new();
    private static void DumpBuildComponentShape(object obj)
    {
        var t=obj.GetType();
        var key=t.FullName??t.Name;
        if(!dumpedBuildTypes.Add(key)) return;
        Log?.LogInfo($"[TST-BUILD] ==== {key} ====");
        try
        {
            foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                var n=p.Name.ToLowerInvariant();
                if(n.Contains("race")||n.Contains("trait")||n.Contains("class")||n.Contains("skill")||n.Contains("affect")||n=="ref")
                    Log?.LogInfo($"[TST-BUILD] PROP {p.Name}:{p.PropertyType.FullName}");
            }
            foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                var n=m.Name.ToLowerInvariant();
                if(n.Contains("race")||n.Contains("trait")||n.Contains("class")||n.Contains("skill")||n.Contains("affect"))
                    Log?.LogInfo($"[TST-BUILD] METHOD {m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))})->{m.ReturnType.FullName}");
            }
        }catch{}
    }

    private static void DumpComponentShape(object component)
    {
        var t=component.GetType();
        var key=t.FullName ?? t.Name;
        if(!dumpedComponentTypes.Add(key)) return;

        Log?.LogInfo($"[TST-COMP-SHAPE] ==== {key} ====");
        try
        {
            foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                var n=m.Name;
                if(n is "SetValue" or "SetProgressionValue" or "AddProgressionValue" or "SetGeneratedValue" or
                        "SetAmount" or "AddItem" or "RemoveItem" or "TryRemoveItemAmount" or "Reload")
                    Log?.LogInfo($"[TST-COMP-SHAPE] METHOD {m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))}) -> {m.ReturnType.FullName}");
            }
        }
        catch(Exception e){ Log?.LogDebug($"[TST-COMP-SHAPE] failed: {e.Message}"); }
    }

    private static void DumpReaderShape(object reader)
    {
        var t=reader.GetType();
        var key=t.FullName ?? t.Name;
        if(!dumpedReaderTypes.Add(key)) return;

        Log?.LogInfo($"[TST-READER-SHAPE] {key}");
        try
        {
            foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                var n=p.Name.ToLowerInvariant();
                if(n.Contains("value")||n.Contains("status")||n.Contains("stat")||n.Contains("item")||n.Contains("slot")||n.Contains("amount")||n.Contains("owner")||n.Contains("entity")||n.Contains("guid")||n.Contains("component"))
                    Log?.LogInfo($"[TST-READER-SHAPE] PROP {p.Name} : {p.PropertyType.FullName}");
            }
            foreach(var fld in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                var n=fld.Name.ToLowerInvariant();
                if(n.Contains("value")||n.Contains("status")||n.Contains("stat")||n.Contains("item")||n.Contains("slot")||n.Contains("amount")||n.Contains("owner")||n.Contains("entity")||n.Contains("guid")||n.Contains("component"))
                    Log?.LogInfo($"[TST-READER-SHAPE] FIELD {fld.Name} : {fld.FieldType.FullName}");
            }
            foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                var n=m.Name.ToLowerInvariant();
                if(n.Contains("value")||n.Contains("status")||n.Contains("stat")||n.Contains("item")||n.Contains("slot")||n.Contains("amount")||n.Contains("component"))
                    Log?.LogInfo($"[TST-READER-SHAPE] METHOD {m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))}) -> {m.ReturnType.FullName}");
            }
        }
        catch(Exception e){ Log?.LogDebug($"[TST-READER-SHAPE] failed: {e.Message}"); }
    }

    private static void DumpMutationApis()
    {
        if(dumpedMutationApis) return;
        dumpedMutationApis=true;

        string[] wanted={"SetValue","AddValue","SetGeneratedValue","AddProgressionValue","SetProgressionValue","AddPermanentValue",
                         "SetAmount","ChangeAmount","AddItem","RemoveItem","TryRemoveItemAmount","SetItemAmount"};
        foreach(var t in GameTypes())
        {
            MethodInfo[] ms;
            try { ms=t.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic); } catch { continue; }
            foreach(var m in ms)
            {
                if(!wanted.Contains(m.Name)) continue;
                var pars=m.GetParameters();
                bool interesting=pars.Any(p=>p.ParameterType.Name=="StatType" || p.ParameterType==typeof(int) || p.ParameterType==typeof(float) ||
                                            (p.ParameterType.FullName??"").Contains("IEntity") || (p.ParameterType.FullName??"").Contains("Guid"));
                if(!interesting) continue;
                Log?.LogInfo($"[TST-MUTATE] {t.FullName}::{m.Name}({string.Join(",",pars.Select(p=>p.ParameterType.FullName))}) -> {m.ReturnType.FullName} static={m.IsStatic}");
            }
        }
    }

    private static object? CastIl2CppObject(object obj,Type targetInterface)
    {
        // Il2CppInterop proxies expose generic Cast<T>() / TryCast<T>() helpers through their base class.
        for(Type? t=obj.GetType(); t!=null; t=t.BaseType)
        {
            MethodInfo[] methods;
            try { methods=t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); }
            catch { continue; }

            foreach(var m in methods)
            {
                try
                {
                    if(!(m.Name=="Cast" || m.Name=="TryCast")) continue;
                    if(!m.IsGenericMethodDefinition || m.GetGenericArguments().Length!=1 || m.GetParameters().Length!=0) continue;
                    var v=m.MakeGenericMethod(targetInterface).Invoke(obj,null);
                    if(v!=null)
                    {
                        Log?.LogInfo($"[TST-CAST] {obj.GetType().FullName} -> {targetInterface.FullName} via {m.Name}<T>()");
                        return v;
                    }
                }
                catch(Exception e)
                {
                    Log?.LogDebug($"[TST-CAST] {m.Name} failed: {e.GetBaseException().Message}");
                }
            }
        }

        // Some generated proxies have constructors accepting IntPtr; reuse the native pointer if exposed.
        try
        {
            var ptrProp=obj.GetType().GetProperty("Pointer",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            var ptr=ptrProp?.GetValue(obj);
            if(ptr is IntPtr p && p!=IntPtr.Zero)
            {
                var ctor=targetInterface.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(IntPtr)},null);
                if(ctor!=null)
                {
                    var v=ctor.Invoke(new object[]{p});
                    Log?.LogInfo($"[TST-CAST] {obj.GetType().FullName} -> {targetInterface.FullName} via IntPtr");
                    return v;
                }
            }
        }
        catch{}

        return null;
    }

    private static object? TryResolveFromProvider(object? provider,Type targetType,string desiredTypeName)
    {
        if(provider==null) return null;

        if(!dumpedProviders.Contains(provider.GetType().FullName ?? provider.GetType().Name))
        {
            var key=provider.GetType().FullName ?? provider.GetType().Name;
            dumpedProviders.Add(key);
            Log?.LogInfo($"[TST-PROVIDER] Inspecting {key} for {desiredTypeName}");
            try
            {
                foreach(var m in provider.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                {
                    var n=m.Name.ToLowerInvariant();
                    if(n.Contains("component")||n.Contains("status")||n.Contains("inventory"))
                        Log?.LogInfo($"[TST-PROVIDER] METHOD {m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))}) -> {m.ReturnType.FullName}");
                }
                foreach(var p in provider.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                {
                    var n=p.Name.ToLowerInvariant();
                    if(n.Contains("component")||n.Contains("status")||n.Contains("inventory"))
                        Log?.LogInfo($"[TST-PROVIDER] PROP {p.Name} : {p.PropertyType.FullName}");
                }
            } catch {}
        }

        foreach(var m in provider.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
        {
            try
            {
                if(m.IsGenericMethodDefinition && m.GetGenericArguments().Length==1 && m.GetParameters().Length==0 &&
                   (m.Name=="GetComponent" || m.Name=="GetComponentOf"))
                {
                    var v=m.MakeGenericMethod(targetType).Invoke(provider,null);
                    if(v!=null) return v;
                }

                if(m.Name=="TryGetComponent")
                {
                    var ps=m.GetParameters();
                    if(ps.Length==2 && ps[0].ParameterType.IsEnum && ps[1].ParameterType.IsByRef)
                    {
                        var names=Enum.GetNames(ps[0].ParameterType);
                        var wanted=names.FirstOrDefault(n=>n.Equals(desiredTypeName.Replace("Component",""),StringComparison.OrdinalIgnoreCase))
                                   ?? names.FirstOrDefault(n=>n.Contains(desiredTypeName.Replace("Component",""),StringComparison.OrdinalIgnoreCase));
                        if(wanted==null) continue;

                        var enumVal=Enum.Parse(ps[0].ParameterType,wanted);
                        object?[] args={enumVal,null};
                        var ok=Convert.ToBoolean(m.Invoke(provider,args));
                        if(ok && args[1]!=null) return args[1];
                    }
                }
            }
            catch{}
        }

        return null;
    }

    private static object? FindComponentInValue(object? value,string desiredTypeName)
    {
        if(value==null) return null;
        if(value.GetType().Name==desiredTypeName || value.GetType().Name.Contains(desiredTypeName.Replace("Component",""),StringComparison.OrdinalIgnoreCase))
            return value;

        if(value is IEnumerable e && value is not string)
        {
            int n=0;
            foreach(var x in e)
            {
                if(x==null) continue;
                if(x.GetType().Name==desiredTypeName || x.GetType().Name.Contains(desiredTypeName.Replace("Component",""),StringComparison.OrdinalIgnoreCase))
                    return x;
                if(++n>64) break;
            }
        }
        return null;
    }

    private static IEnumerable<object> FindObjectsOfType(Type t)
    {
        var result=new List<object>();

        try
        {
            var objType = FindType("UnityEngine.Object");
            if (objType != null)
            {
                var findMethod = objType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "FindObjectsOfType" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));
                
                if (findMethod != null && findMethod.Invoke(null, new object[]{t}) is IEnumerable eObj)
                {
                    foreach(var x in eObj) if(x!=null) result.Add(x);
                    if (result.Count > 0) return result;
                }
            }

            if(resourcesType!=null) 
            {
                var m=resourcesType.GetMethods(BindingFlags.Public|BindingFlags.Static)
                    .FirstOrDefault(x=>x.Name=="FindObjectsOfTypeAll" && x.GetParameters().Length==1 && x.GetParameters()[0].ParameterType==typeof(Type));
                if(m?.Invoke(null,new object[]{t}) is IEnumerable e)
                    foreach(var x in e) if(x!=null) result.Add(x);
            }
        }
        catch{}
        return result;
    }

    private sealed class MissingComponent {}

    public static int? GetGold()
    {
        if (clan == null) return null;
        try
        {
            var getter=clan.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .FirstOrDefault(m=>m.Name=="get_ClanGold" && m.GetParameters().Length==0);
            if(getter!=null) return Convert.ToInt32(getter.Invoke(clan,null));

            var v=GetMember(clan,"ClanGold");
            return v==null ? null : Convert.ToInt32(v);
        }
        catch { return null; }
    }

    public static void SetGold(int value)
    {
        if (clan == null) { Log?.LogWarning("[TST-GOLD] clan object missing"); return; }
        try
        {
            int current = GetGold() ?? 0;
            int delta=value-current;
            var add = clan.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "AddClanGold" && m.GetParameters().Length == 1);

            if (add != null)
            {
                var p = add.GetParameters()[0].ParameterType;
                add.Invoke(clan, new[] { Convert.ChangeType(delta, p) });
                var after=GetGold();
                Log?.LogInfo($"[TST-GOLD] AddClanGold delta={delta} before={current} after={(after?.ToString() ?? "null")}");
                goldText=(after ?? value).ToString();
                return;
            }

            // IL2CPP exposes ClanGold as a generated field accessor. Try all known backing names directly.
            foreach(var n in new[]{"ClanGold","_ClanGold_k__BackingField","ClanGold_k__BackingField"})
            {
                if(SetMember(clan,n,value))
                {
                    var after=GetGold();
                    Log?.LogInfo($"[TST-GOLD] field {n} before={current} after={(after?.ToString() ?? "null")}");
                    goldText=(after ?? value).ToString();
                    return;
                }
            }
            Log?.LogWarning($"[TST-GOLD] no writable gold path on {clan.GetType().FullName}");
        }
        catch (Exception e) { Log?.LogWarning($"[TST-GOLD] {e.GetBaseException().Message}"); }
    }

    public static float? GetCharacterStat(int i, string name)
    {
        var r = At(statusReaders, i);
        if(r!=null && r is not MissingComponent)
        {
            var v=ProbeReaderValue(r,name) ?? ReadNamedStat(r,name) ?? ReadNamedStat(GetMember(r,"StateStats"),name);
            var f=ToFloat(v); if(f.HasValue) return f;
        }
        var s = At(statuses, i); if (s == null || s is MissingComponent) return null;
        return ToFloat(ReadNamedStat(GetMember(s, "StateStats"), name) ?? ReadNamedStat(s,name));
    }

    public static void SetCharacterStat(int i, string name, float value)
    {
        var r=At(statusReaders,i);
        if(r!=null && r is not MissingComponent && TryInvokeStatMutation(r,"SetValue",name,value)) return;

        var s = At(statuses, i); if (s == null || s is MissingComponent) return;
        if(TryInvokeStatMutation(s,"SetValue",name,value)) return;
        WriteNamedStat(GetMember(s, "StateStats"), name, value);
    }

    public static float? GetCharacterProgress(int i, string name)
    {
        var r=At(statusReaders,i);
        if(r!=null && r is not MissingComponent)
        {
            var v=ProbeReaderValue(r,name) ?? ReadNamedStat(r,name) ?? ReadNamedStat(GetMember(r,"ProgressionStats"),name);
            var f=ToFloat(v); if(f.HasValue) return f;
        }
        var s = At(statuses, i); if (s == null || s is MissingComponent) return null;
        return ToFloat(ReadNamedStat(GetMember(s, "ProgressionStats"), name) ?? ReadNamedStat(s,name));
    }

    public static void SetCharacterProgress(int i, string name, float value)
    {
        var r=At(statusReaders,i);
        if(r!=null && r is not MissingComponent)
        {
            if(TryInvokeStatMutation(r,"SetProgressionValue",name,value)) return;
            if(TryInvokeStatMutation(r,"SetValue",name,value)) return;
        }

        var s = At(statuses, i); if (s == null || s is MissingComponent) return;
        if(TryInvokeStatMutation(s,"SetProgressionValue",name,value)) return;
        if(TryInvokeStatMutation(s,"SetValue",name,value)) return;
        WriteNamedStat(GetMember(s, "ProgressionStats"), name, value);
    }

    public static float? GetGeneratedStat(int i,string name)
    {
        var r=At(statusReaders,i);
        if(r==null || r is MissingComponent) return null;
        var v=InvokeReaderStat(r,name);
        return ToFloat(v);
    }

    public static void SetGeneratedStat(int i,string name,float value)
    {
        var s=At(statuses,i);
        if(s==null || s is MissingComponent) return;
        if(TryInvokeStatMutation(s,"SetGeneratedValue",name,value))
        {
            try { At(statusReaders,i)?.GetType().GetMethod("Reload",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(At(statusReaders,i),null); } catch{}
        }
    }

    private static string? ReadProfileRace(object? profile)
    {
        if(profile==null) return null;
        foreach(var n in new[]{"Race","RaceType"})
        {
            var v=GetMember(profile,n);
            if(v!=null) return v.ToString();
        }
        foreach(var n in new[]{"GetRaceType","get_Race"})
        {
            try
            {
                var m=profile.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .FirstOrDefault(x=>x.Name==n && x.GetParameters().Length==0);
                var v=m?.Invoke(profile,null);
                if(v!=null) return v.ToString();
            } catch{}
        }
        return null;
    }

    private static void EnforceGodMode(int i)
    {
        SetCharacterStat(i, "HealthHead", 100f);
        SetCharacterStat(i, "HealthBody", 100f);
    }

    private static bool TryInvokeStatMutation(object target,string methodName,string statName,float uiValue)
    {
        var statType=GameTypes().FirstOrDefault(t=>t.Name=="StatType" && t.IsEnum);
        if(statType==null) return false;

        string[] aliases=statName switch
        {
            "HealthBody" => new[]{"HealthBody","BodyHealth","Health"},
            "HealthHead" => new[]{"HealthHead","HeadHealth"},
            "MainSkillPoint" => new[]{"MainSkillPoint","MainSkillPoints"},
            "SubSkillPoint" => new[]{"SubSkillPoint","SubSkillPoints"},
            "Exp" => new[]{"Exp","Experience"},
            _ => new[]{statName}
        };

        object? ev=null;
        foreach(var a in aliases)
        {
            var n=Enum.GetNames(statType).FirstOrDefault(x=>x.Equals(a,StringComparison.OrdinalIgnoreCase));
            if(n!=null){ ev=Enum.Parse(statType,n); break; }
        }
        if(ev==null) return false;

        foreach(var m in target.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
        {
            if(m.Name!=methodName) continue;
            var ps=m.GetParameters();
            if(ps.Length!=2 || ps[0].ParameterType.Name!="StatType") continue;
            try
            {
                float raw=uiValue;
                // state stats use centi-units in the live reader
                if(statName is "HealthBody" or "HealthHead" or "Energy" or "Hunger" or "Stress") raw=uiValue*100f;
                var converted=Convert.ChangeType(raw,ps[1].ParameterType);
                m.Invoke(target,new[]{ev,converted});
                Log?.LogInfo($"[TST-WRITE] {target.GetType().FullName}::{methodName} {statName}={raw}");
                try
                {
                    var reload=target.GetType().GetMethod("Reload",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    reload?.Invoke(target,null);
                }
                catch{}
                return true;
            }
            catch(Exception e){ Log?.LogDebug($"[TST-WRITE] {methodName} {statName} failed: {e.GetBaseException().Message}"); }
        }
        return false;
    }

    private static void SetGameSpeed(float value)
    {
        try { timeScaleProp?.SetValue(null, value); }
        catch (Exception e) { Log?.LogWarning(e); }
    }

    private static IEnumerable<object> EnumerateAny(object? source)
    {
        var result=new List<object>();
        if(source==null) return result;

        if(source is IEnumerable normal)
        {
            foreach(var x in normal) if(x!=null) result.Add(x);
            return result;
        }

        var t=source.GetType();
        var countProp=t.GetProperty("Count",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic) ??
                      t.GetProperty("Length",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        var itemProp=t.GetProperty("Item",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        if(countProp!=null && itemProp!=null)
        {
            int count=0; try{count=Convert.ToInt32(countProp.GetValue(source));}catch{}
            for(int i=0;i<count;i++)
            {
                object? x=null; try{x=itemProp.GetValue(source,new object[]{i});}catch{}
                if(x!=null) result.Add(x);
            }
            return result;
        }

        try
        {
            var getEnum=t.GetMethod("GetEnumerator",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            var en=getEnum?.Invoke(source,null);
            if(en!=null)
            {
                var et=en.GetType();
                var move=et.GetMethod("MoveNext",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                var current=et.GetProperty("Current",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                int guard=0;
                while(move!=null && current!=null && guard++<2048)
                {
                    bool ok=false; try{ok=Convert.ToBoolean(move.Invoke(en,null));}catch{break;}
                    if(!ok) break;
                    object? x=null; try{x=current.GetValue(en);}catch{}
                    if(x!=null) result.Add(x);
                }
            }
        }
        catch{}

        return result;
    }

    private static Dictionary<string,int> GetSettlementResources()
    {
        var totals=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        if(settlementItemContainer==null) return totals;

        object? source=null;
        var t=settlementItemContainer.GetType();

        foreach(var name in new[]{"GetAllEntities","GetAll","GetAllItems","GetEnumerable"})
        {
            try
            {
                var m=t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .FirstOrDefault(x=>x.Name==name && x.GetParameters().Length==0);
                if(m!=null) { source=m.Invoke(settlementItemContainer,null); if(source!=null) break; }
            } catch{}
        }

        foreach(var entry in EnumerateAny(source))
        {
            if(entry==null) continue;
            var item=GetMember(entry,"Item1") ?? GetMember(entry,"Value") ?? GetMember(entry,"Item") ?? entry;
            var key=(GetMember(item,"Key") ?? GetMember(item,"ItemKey") ?? GetMember(item,"ProfileKey") ?? GetMember(item,"DataKey") ?? "").ToString() ?? "";
            if(string.IsNullOrWhiteSpace(key) || !key.StartsWith("ITEM_",StringComparison.OrdinalIgnoreCase)) continue;

            int amount=1;
            var av=GetMember(entry,"Item2") ?? GetMember(entry,"Amount") ?? GetMember(item,"Amount") ?? GetMember(item,"Count");
            if(av!=null) try{amount=Convert.ToInt32(av);}catch{}
            totals[key]=totals.TryGetValue(key,out var old)?old+Math.Max(0,amount):Math.Max(0,amount);
        }
        if(totals.Count==0 && source!=null)
        {
            var first=EnumerateAny(source).FirstOrDefault();
            if(first!=null) DumpInterestingObject("[TST-ITEMENTITY]",first);
        }
        return totals;
    }

    private static readonly HashSet<string> dumpedInterestingTypes=new();
    private static void DumpInterestingObject(string prefix,object obj)
    {
        var t=obj.GetType(); var key=prefix+(t.FullName??t.Name);
        if(!dumpedInterestingTypes.Add(key)) return;
        Log?.LogInfo($"{prefix} TYPE {t.FullName}");
        try
        {
            foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                var n=p.Name.ToLowerInvariant();
                if(n.Contains("key")||n.Contains("amount")||n.Contains("guid")||n.Contains("type")||n.Contains("profile")||n.Contains("race")||n.Contains("trait"))
                {
                    object? v=null; try{v=p.GetValue(obj);}catch{}
                    Log?.LogInfo($"{prefix} PROP {p.Name}:{p.PropertyType.FullName}={v}");
                }
            }
            foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            {
                var n=m.Name.ToLowerInvariant();
                if(n.Contains("key")||n.Contains("amount")||n.Contains("race")||n.Contains("trait"))
                    Log?.LogInfo($"{prefix} METHOD {m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.Name))})->{m.ReturnType.Name}");
            }
        }catch{}
    }

    internal sealed class InvRow
    {
        public object Raw = default!;
        public string Name = "";
        public int Amount;
        public int Index;
    }

    public static List<InvRow> GetInventory(int i)
    {
        var result = new List<InvRow>();
        var inv = At(inventoryReaders, i);
        if(inv==null || inv is MissingComponent) inv=At(inventories,i);
        if (inv == null || inv is MissingComponent) return result;

        object? source=null;
        foreach(var methodName in new[]{"GetAllItems","GetAllItemInfos","GetEnumerable"})
        {
            try
            {
                var m=inv.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .FirstOrDefault(x=>x.Name==methodName && x.GetParameters().Length==0);
                if(m!=null)
                {
                    source=m.Invoke(inv,null);
                    if(source is IEnumerable) break;
                }
            }
            catch(Exception e){ Log?.LogDebug($"[TST-INV] {methodName} failed: {e.GetBaseException().Message}"); }
        }

        source ??= GetMember(inv, "ItemSlots") ?? GetMember(inv,"Slots") ?? GetMember(inv,"Items");

        if(source is IEnumerable enumerable)
        {
            int index=0;
            foreach(var holder in enumerable)
            {
                if(holder==null) { index++; continue; }

                var itemObj=GetMember(holder,"Item") ?? GetMember(holder,"ItemEntity") ?? GetMember(holder,"Entity");
                var key=(GetMember(holder,"ItemKey") ?? GetMember(holder,"Key") ??
                         GetMember(itemObj,"ItemKey") ?? GetMember(itemObj,"Key") ??
                         GetMember(itemObj,"ProfileKey") ?? "").ToString() ?? "";

                int amount=0;
                foreach(var amountName in new[]{"Amount","Count","Stack","ItemAmount"})
                {
                    var av=GetMember(holder,amountName) ?? GetMember(itemObj,amountName);
                    if(av==null) continue;
                    try { amount=Convert.ToInt32(av); break; } catch {}
                }

                if(string.IsNullOrWhiteSpace(key))
                {
                    var guid=GetMember(holder,"ItemGuid") ?? GetMember(itemObj,"Guid");
                    if(guid!=null) key=$"Item {index+1} ({guid})";
                    else { index++; continue; }
                }

                if(amount<=0) amount=1;
                result.Add(new InvRow { Raw = holder, Name = key.Replace("ITEM_", ""), Amount = amount, Index = index });
                index++;
            }
        }

        Log?.LogDebug($"[TST-INV] player {i} rows={result.Count}");
        return result;
    }

    public static void SetInventoryAmount(InvRow row, int amount)
    {
        amount=Math.Max(0,amount);
        var inv=At(inventories,selectedCharacter);
        var reader=At(inventoryReaders,selectedCharacter);
        if(inv==null || inv is MissingComponent) return;

        try
        {
            // InventoryComponent exposes AddItem(index, guid, key, amount, bool).
            // Replacing the slot through the component is authoritative; mutating ItemHolder alone is only a copy/view.
            var add=inv.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .FirstOrDefault(m=>m.Name=="AddItem" && m.GetParameters().Length==5);
            var remove=inv.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .FirstOrDefault(m=>m.Name=="RemoveItem" && m.GetParameters().Length==1);

            if(amount==0 && remove!=null)
            {
                remove.Invoke(inv,new object[]{row.Index});
                Log?.LogInfo($"[TST-INV-WRITE] RemoveItem index={row.Index}");
            }
            else if(add!=null)
            {
                var guid=GetMember(row.Raw,"ItemGuid") ?? GetMember(row.Raw,"Guid");
                var key=(GetMember(row.Raw,"ItemKey") ?? GetMember(row.Raw,"Key") ?? row.Name).ToString() ?? row.Name;

                if(guid==null)
                {
                    Log?.LogWarning($"[TST-INV-WRITE] No guid for {row.Name}");
                    return;
                }

                var ps=add.GetParameters();
                object?[] args={
                    Convert.ChangeType(row.Index,ps[0].ParameterType),
                    guid,
                    key,
                    Convert.ChangeType(amount,ps[3].ParameterType),
                    Convert.ChangeType(false,ps[4].ParameterType)
                };
                add.Invoke(inv,args);
                Log?.LogInfo($"[TST-INV-WRITE] AddItem index={row.Index} key={key} amount={amount}");
            }

            try { reader?.GetType().GetMethod("Reload",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(reader,null); } catch {}
        }
        catch(Exception e){ Log?.LogWarning($"[TST-INV-WRITE] {e.GetBaseException().Message}"); }
    }

    private static object? At(List<object> list, int i) => i >= 0 && i < list.Count ? list[i] : null;

    private static object? GetMember(object? obj, string name)
    {
        if (obj == null) return null;
        var t = obj.GetType();
        var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null) try { return p.GetValue(obj); } catch { }
        var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) try { return f.GetValue(obj); } catch { }
        return null;
    }

    private static bool SetMember(object? obj, string name, object value)
    {
        if (obj == null) return false;
        var t = obj.GetType();
        var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p?.CanWrite == true)
        {
            try { p.SetValue(obj, Convert.ChangeType(value, p.PropertyType)); return true; } catch { }
        }

        var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null)
        {
            try { f.SetValue(obj, Convert.ChangeType(value, f.FieldType)); return true; } catch { }
        }
        return false;
    }

    private static object? ReadNamedStat(object? stats, string name)
    {
        if (stats == null) return null;

        var direct = GetMember(stats, name);
        if (direct != null) return direct;

        if (stats is IDictionary dict)
        {
            foreach (DictionaryEntry e in dict)
                if (e.Key?.ToString() == name) return e.Value;
        }

        var t = stats.GetType();
        var keys = t.GetProperty("Keys")?.GetValue(stats) as IEnumerable;
        var indexer = t.GetProperty("Item");
        if (keys != null && indexer != null)
        {
            foreach (var k in keys)
            {
                if (k?.ToString() != name) continue;
                try { return indexer.GetValue(stats, new[] { k }); } catch { }
            }
        }

        return null;
    }

    private static void WriteNamedStat(object? stats, string name, float value)
    {
        if (stats == null) return;
        if (SetMember(stats, name, value)) return;

        var t = stats.GetType();
        var keys = t.GetProperty("Keys")?.GetValue(stats) as IEnumerable;
        var indexer = t.GetProperty("Item");
        if (keys == null || indexer == null) return;

        foreach (var k in keys)
        {
            if (k?.ToString() != name) continue;
            try
            {
                var old = indexer.GetValue(stats, new[] { k });
                var converted = old == null ? value : Convert.ChangeType(value, old.GetType());
                indexer.SetValue(stats, converted, new[] { k });
                return;
            }
            catch { }
        }
    }

    private static float? ToFloat(object? value)
    {
        if (value == null) return null;
        try { return Convert.ToSingle(value); } catch { return null; }
    }
}
