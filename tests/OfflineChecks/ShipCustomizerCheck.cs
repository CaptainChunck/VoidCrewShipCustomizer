using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

// Offline checks for the Ship Customizer (no game running):
//  1. every patch target, private field and injected parameter the plugin looks up BY NAME exists in the game's
//     Assembly-CSharp.dll, with exactly one method of that name (AccessTools.Method would be ambiguous otherwise);
//  2. the plugin's hardpoint rules give the expected answers on a made-up ship layout;
//  3. a Custom loadout config survives a JSON round trip with the game's Newtonsoft;
//  4. the public API other plugins (Void Crew Fleet) call by reflection keeps its names and signatures;
//  5. Void Crew Fleet's own API has the members this plugin looks up by name (when Fleet's DLL is there), and without
//     Fleet everything for a second ship stays off.
// Run it with tests\build-and-test.ps1, which builds the plugin first. When the plugin starts looking up another game
// member by name (a Harmony patch target, a ___field injection, AccessTools.Field/Method), add a check for it below.
// Usage: ShipCustomizerCheck.exe <plugin dir> <game Managed dir> <BepInEx core dir> [<desktop Newtonsoft.Json dir> [<VoidCrewFleet.dll>]]
internal static class Program
{
    private static string[] _dirs;
    private static int _failures;

    private static int Main(string[] args)
    {
        _dirs = args;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try
        {
            StaticChecks(Path.Combine(args[1], "Assembly-CSharp.dll"));
            ApiChecks(Path.Combine(args[0], "VoidCrewShipCustomizer.dll"));
            FleetApiChecks(args.Length > 4 ? args[4] : null);
            LogicChecks(Assembly.LoadFrom(Path.Combine(args[0], "VoidCrewShipCustomizer.dll")));
        }
        catch (Exception e)
        {
            Console.WriteLine("FAILED: " + e);
            return 2;
        }
        Console.WriteLine(_failures == 0 ? "ALL CHECKS PASSED" : _failures + " CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        string file = new AssemblyName(e.Name).Name + ".dll";
        // Unity's Newtonsoft.Json fails strong-name validation on the desktop runtime; an optional 4th folder supplies
        // a desktop build of the same version for the save-format test.
        if (file == "Newtonsoft.Json.dll" && _dirs.Length > 3 && File.Exists(Path.Combine(_dirs[3], file))) return Assembly.LoadFrom(Path.Combine(_dirs[3], file));
        foreach (string dir in _dirs)
        {
            string path = Path.Combine(dir, file);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _failures++;
    }

    // ------------------------------------------------------------------ 1. names in the game assembly

    private static ModuleDefinition _game;

    private static void StaticChecks(string gamePath)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(gamePath));
        _game = ModuleDefinition.ReadModule(gamePath, new ReaderParameters { AssemblyResolver = resolver });

        Console.WriteLine("Patch targets (type, method, injected parameters):");
        Method("HubShipManager", "SelectShip", "guid");
        Method("HubShipManager", "get_CurrentShipSelected");
        Method("HubShipManager", "NotifyShipSelected", "guid");
        Method("HubShipManager", "Start");
        Method("Gameplay.Hub.HubQuestManager", "SelectPreservedSessionQuest");
        Method("CG.GameLoopStateMachine.GameStates.GSIngame", "TransitionTo", "gameSession", "preservedGameSession");
        Method("UI.ShipLoadoutTerminal.LoadoutList", "SetSource", "loadouts");
        Method("UI.ShipLoadoutTerminal.LoadoutList", "Sort", "a", "b");
        Method("UI.ShipLoadoutTerminal.LoadoutList", "BindItem", "item", "loadout");
        Method("UI.ShipLoadoutTerminal.ModuleLayout", "Show", "loadout", "data");
        Method("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "Display", "def");
        Method("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "OnDestroy");
        Method("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "OnLeaveUI");
        Method("CG.Profile.PlayerRewardsCollection", "AddItem");
        Method("CG.Cloud.CloudRewardsProfileSync", "AddItem");
        Method("CG.Profile.PlayerAchievements", "SetAchievementComplete");
        Method("SteamPlayerAchievementsSync", "SetAchievementComplete");
        Method("CG.Cloud.CloudPlayerAchievementsSync", "SetAchievementComplete");
        Method("CG.Profile.PlayerProfileData", "AddQuestProgress");
        Method("CG.Cloud.CloudPlayerProfileDataSync", "AddQuestProgress");
        Method("CG.Profile.PhotonPlayerDataSync", "AddQuestProgress");
        Method("CG.Ship.Hull.CarryablesSocket", "DoesAccept", "carryable");
        Method("ShipLoadout", "InitializeShip", "Position", "Rotation");
        // 0.8.0: the rest of progression (tracked stats, Steam stats, daily and weekly quests), and saved CUSTOM runs that
        // wait for everyone to have the mod.
        Method("Gameplay.Hub.HubQuestManager", "StartQuest", "startType");
        Method("TrackedMetric", "OnMetricProgressChange");
        Method("CG.Profile.PlayerTrackedMetrics", "SubmitProgress");
        Method("CloudPlayerTrackedMetricSync", "SubmitProgress");
        Method("SteamPlayerTrackedMetrics", "SubmitProgress");
        Method("Metaquest", "OnMetricProgress");

        Console.WriteLine("Fields read by name (Harmony ___field injection and reflection):");
        Field("UI.ShipLoadoutTerminal.ModuleLayout", "slots", "System.Collections.Generic.List`1<UI.PowerTerminal.SlotVE>");
        Field("UI.ShipLoadoutTerminal.ModuleLayout", "selectButton", "UnityEngine.UIElements.Button");
        Field("UI.ShipLoadoutTerminal.ModuleLayout", "moduleLayoutAnchor", "UnityEngine.UIElements.VisualElement");
        Field("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "root", "UnityEngine.UIElements.VisualElement");
        Field("HubShipManager", "_currentShipSelected", "ResourceAssets.ShipLoadoutDataDef");
        Field("HubShipManager", "ShipSelectionChanged", "System.Action`1<ResourceAssets.ShipLoadoutDataDef>");
        Field("Client.Player.Interactions.ContextInfo", "headerTextDefaultable", "DefaultableLocalizedString");
        Field("Client.Player.Interactions.ContextInfo", "bodyTextDefaultable", "DefaultableLocalizedString");
        Field("CG.Client.Utils.CsObjectReference", "compareMethod", "CG.Client.Utils.CsObjectReference/Comparison");
        Field("CG.Client.Utils.CsObjectReference", "type", null);
        Field("CG.Client.Utils.CsObjectReference", "orbitObjectRef", null);

        Console.WriteLine("Types found by name:");
        TypeDefinition fabricator = _game.GetType("CG.Ship.Modules.FabricatorModule");
        Check(fabricator != null, "CG.Ship.Modules.FabricatorModule exists (a Fleet Battle never allows extra Fabricator modules)");
    }

    private static TypeDefinition Type(string fullName)
    {
        TypeDefinition type = _game.GetType(fullName);
        if (type == null) Check(false, "type " + fullName + " exists");
        return type;
    }

    private static void Method(string typeName, string methodName, params string[] parameters)
    {
        TypeDefinition type = Type(typeName);
        if (type == null) return;
        List<MethodDefinition> methods = type.Methods.Where(m => m.Name == methodName).ToList();
        string what = typeName + "." + methodName + "(" + string.Join(", ", parameters) + ")";
        if (methods.Count != 1)
        {
            Check(false, what + ": expected exactly one method with that name, found " + methods.Count);
            return;
        }
        string[] names = methods[0].Parameters.Select(p => p.Name).ToArray();
        string[] missing = parameters.Where(p => !names.Contains(p)).ToArray();
        Check(missing.Length == 0, what + (missing.Length == 0 ? "" : ": missing parameter(s) " + string.Join(", ", missing) + "; it has " + string.Join(", ", names)));
    }

    private static void Field(string typeName, string fieldName, string fieldType)
    {
        TypeDefinition type = Type(typeName);
        if (type == null) return;
        FieldDefinition field = type.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (field == null)
        {
            Check(false, typeName + "." + fieldName + " exists");
            return;
        }
        bool typeOk = fieldType == null || field.FieldType.FullName == fieldType;
        Check(typeOk, typeName + "." + fieldName + " : " + field.FieldType.FullName + (typeOk ? "" : " (expected " + fieldType + ")"));
    }

    // ------------------------------------------------------------------ 4. the public API (read from the DLL, not loaded)

    private static void ApiChecks(string pluginPath)
    {
        Console.WriteLine("Public API for other plugins (Void Crew Fleet calls these by reflection):");
        ModuleDefinition plugin = ModuleDefinition.ReadModule(pluginPath);
        TypeDefinition api = plugin.GetType("VoidCrewShipCustomizer.ShipCustomizerApi");
        if (api == null)
        {
            Check(false, "VoidCrewShipCustomizer.ShipCustomizerApi exists");
            return;
        }
        Check(api.IsPublic && api.IsAbstract && api.IsSealed, "ShipCustomizerApi is a public static class");
        FieldDefinition version = api.Fields.FirstOrDefault(f => f.Name == "ApiVersion");
        Check(version != null && version.IsPublic && version.HasConstant && Equals(version.Constant, 1), "ApiVersion is a public const int = 1");
        ApiMethod(api, "GetDef", "ResourceAssets.ShipLoadoutDataDef", "GUIDUnion");
        ApiMethod(api, "GetStandIn", "ResourceAssets.ShipLoadoutDataDef", "GUIDUnion");
        ApiMethod(api, "SetTerminalShip", "System.Void", "System.Int32");
        ApiMethod(api, "CheckShip2", "System.String", "GUIDUnion", "System.Int32");
        ApiMethod(api, "SetShip2", "System.Void", "GUIDUnion");
        ApiMethod(api, "BuildShip2", "ShipLoadout", "GUIDUnion");
        ApiMethod(api, "GetShip2Alloys", "System.Int32", "GUIDUnion");
        ApiMethod(api, "GetRuleOptions", "System.String[]");
        EventDefinition cancelled = api.Events.FirstOrDefault(e => e.Name == "Ship2CustomCancelled");
        Check(cancelled != null && cancelled.EventType.FullName == "System.Action" && cancelled.AddMethod.IsPublic && cancelled.AddMethod.IsStatic,
            "public static event System.Action Ship2CustomCancelled");
    }

    // ------------------------------------------------------------------ 5. Void Crew Fleet's API (read from its DLL, not loaded)

    private static void FleetApiChecks(string fleetPath)
    {
        Console.WriteLine("Void Crew Fleet's team API (FleetLink looks these up by name):");
        if (string.IsNullOrEmpty(fleetPath) || !File.Exists(fleetPath))
        {
            Console.WriteLine("  skip Void Crew Fleet's DLL wasn't found" + (string.IsNullOrEmpty(fleetPath) ? "" : " at " + fleetPath));
            return;
        }
        TypeDefinition api = ModuleDefinition.ReadModule(fleetPath).GetType("VoidCrewFleet.FleetApi");
        if (api == null)
        {
            Check(false, "VoidCrewFleet.FleetApi exists");
            return;
        }
        FieldDefinition version = api.Fields.FirstOrDefault(f => f.Name == "ApiVersion");
        Check(version != null && version.IsPublic && version.HasConstant && Equals(version.Constant, 1), "FleetApi.ApiVersion is a public const int = 1");
        PropertyDefinition teamMatch = api.Properties.FirstOrDefault(p => p.Name == "IsTeamMatch");
        Check(teamMatch != null && teamMatch.GetMethod != null && teamMatch.GetMethod.IsPublic && teamMatch.GetMethod.IsStatic && teamMatch.PropertyType.FullName == "System.Boolean",
            "public static bool IsTeamMatch { get; }");
        ApiMethod(api, "TeamOfShip", "System.Int32", "System.Int32");
        ApiMethod(api, "CanEditShip", "System.Boolean", "System.Int32", "System.Int32");
        ApiMethod(api, "ShipColor", "UnityEngine.Color", "System.Int32");
        EventDefinition changed = api.Events.FirstOrDefault(e => e.Name == "TeamsChanged");
        Check(changed != null && changed.EventType.FullName == "System.Action" && changed.AddMethod.IsPublic && changed.AddMethod.IsStatic,
            "public static event System.Action TeamsChanged");

        // Fleet Battle's match rules for CUSTOM ships (Void Crew Fleet 0.3.0 on); FleetLink uses them only when all are there.
        Console.WriteLine("Void Crew Fleet's match rules API (FleetLink looks these up by name):");
        if (api.Properties.All(p => p.Name != "ShipRulesActive"))
        {
            Console.WriteLine("  skip this Void Crew Fleet build has no match rules (older than 0.3.0)");
            return;
        }
        ApiProperty(api, "ShipRulesActive", "System.Boolean");
        ApiProperty(api, "FabricatorMaxLevel", "System.Int32");
        ApiProperty(api, "MaxStartAlloys", "System.Int32");
        ApiMethod(api, "IsTierAllowed", "System.Boolean", "System.Int32");
        ApiMethod(api, "IsCategoryAllowed", "System.Boolean", "System.String");
        ApiMethod(api, "IsItemAllowed", "System.Boolean", "System.String");
        EventDefinition rulesChanged = api.Events.FirstOrDefault(e => e.Name == "ShipRulesChanged");
        Check(rulesChanged != null && rulesChanged.EventType.FullName == "System.Action" && rulesChanged.AddMethod.IsPublic && rulesChanged.AddMethod.IsStatic,
            "public static event System.Action ShipRulesChanged");
    }

    private static void ApiProperty(TypeDefinition api, string name, string type)
    {
        PropertyDefinition property = api.Properties.FirstOrDefault(p => p.Name == name);
        Check(property != null && property.GetMethod != null && property.GetMethod.IsPublic && property.GetMethod.IsStatic && property.PropertyType.FullName == type,
            "public static " + type + " " + name + " { get; }");
    }

    private static void ApiMethod(TypeDefinition api, string name, string returns, params string[] parameters)
    {
        string what = "public static " + returns + " " + name + "(" + string.Join(", ", parameters) + ")";
        List<MethodDefinition> methods = api.Methods.Where(m => m.Name == name).ToList();
        if (methods.Count != 1)
        {
            Check(false, what + ": expected exactly one method with that name, found " + methods.Count);
            return;
        }
        MethodDefinition method = methods[0];
        bool ok = method.IsPublic && method.IsStatic && method.ReturnType.FullName == returns &&
                  method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters);
        Check(ok, what + (ok ? "" : ": found " + method.FullName));
    }

    // ------------------------------------------------------------------ 2 and 3. plugin logic

    private static Type _socketType;
    private static Type _hardpoints;
    private static Type _buildSize;

    private static void LogicChecks(Assembly plugin)
    {
        _socketType = plugin.GetType("VoidCrewShipCustomizer.SocketInfo", true);
        _hardpoints = plugin.GetType("VoidCrewShipCustomizer.Hardpoints", true);
        _buildSize = System.Type.GetType("CG.Ship.Hull.BuildSize, Assembly-CSharp", true);
        Type vector3 = System.Type.GetType("UnityEngine.Vector3, UnityEngine.CoreModule", true);

        // A made-up ship: two 2x1 hardpoints (each with the two 1x1 positions inside it), one 2x2 hardpoint with four
        // 1x1 positions and a 2x1 position across its front row, and a lone 1x1 hardpoint.
        object right = Activator.CreateInstance(vector3, 1f, 0f, 0f);
        object forward = Activator.CreateInstance(vector3, 0f, 0f, 1f);
        IList sockets = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_socketType));
        AddSocket(sockets, vector3, right, forward, "Medium", 0, 0, 0);    // #0
        AddSocket(sockets, vector3, right, forward, "Small", -2, 0, 0);    // #1
        AddSocket(sockets, vector3, right, forward, "Small", 2, 0, 0);     // #2
        AddSocket(sockets, vector3, right, forward, "Medium", 0, 0, 20);   // #3
        AddSocket(sockets, vector3, right, forward, "Small", -2, 0, 20);   // #4
        AddSocket(sockets, vector3, right, forward, "Small", 2, 0, 20);    // #5
        AddSocket(sockets, vector3, right, forward, "Large", 0, 0, 40);    // #6 cells (-2,38) (2,38) (-2,42) (2,42)
        AddSocket(sockets, vector3, right, forward, "Small", -2, 0, 38);   // #7
        AddSocket(sockets, vector3, right, forward, "Small", 2, 0, 38);    // #8
        AddSocket(sockets, vector3, right, forward, "Small", -2, 0, 42);   // #9
        AddSocket(sockets, vector3, right, forward, "Small", 2, 0, 42);    // #10
        AddSocket(sockets, vector3, right, forward, "Medium", 0, 0, 38);   // #11 cells (-2,38) (2,38)
        AddSocket(sockets, vector3, right, forward, "Small", 30, 0, 0);    // #12 on its own

        Console.WriteLine("Hardpoint rules on a made-up ship:");
        int groups = (int)_hardpoints.GetMethod("LinkOverlaps").Invoke(null, new object[] { sockets });
        Check(groups == 4, "4 hardpoints found (got " + groups + ")");
        Check(Overlaps(sockets, 0).SequenceEqual(new[] { 1, 2 }), "2x1 #0 overlaps its 1x1 positions #1 and #2 (got " + string.Join(",", Overlaps(sockets, 0)) + ")");
        Check(Overlaps(sockets, 1).SequenceEqual(new[] { 0 }), "1x1 #1 overlaps only #0 (got " + string.Join(",", Overlaps(sockets, 1)) + ")");
        Check(Overlaps(sockets, 6).SequenceEqual(new[] { 7, 8, 9, 10, 11 }), "2x2 #6 overlaps #7-#11 (got " + string.Join(",", Overlaps(sockets, 6)) + ")");
        Check(Overlaps(sockets, 11).SequenceEqual(new[] { 6, 7, 8 }), "2x1 #11 overlaps #6, #7, #8 (got " + string.Join(",", Overlaps(sockets, 11)) + ")");
        Check(Overlaps(sockets, 12).Length == 0, "lone 1x1 #12 overlaps nothing");

        Check(Offered(sockets, new int[0]).SequenceEqual(new[] { 0, 3, 6, 12 }), "empty ship shows one + per hardpoint: #0 #3 #6 #12 (got " + string.Join(",", Offered(sockets, new int[0])) + ")");
        Check(Offered(sockets, new[] { 1 }).SequenceEqual(new[] { 2, 3, 6, 12 }), "1x1 in #1: the other half #2 is offered, #0 is not (got " + string.Join(",", Offered(sockets, new[] { 1 })) + ")");
        Check(Offered(sockets, new[] { 0 }).SequenceEqual(new[] { 3, 6, 12 }), "2x1 in #0: its halves are not offered (got " + string.Join(",", Offered(sockets, new[] { 0 })) + ")");
        Check(Offered(sockets, new[] { 11 }).SequenceEqual(new[] { 0, 3, 9, 10, 12 }), "2x1 across the 2x2's front: the back row #9 #10 is offered (got " + string.Join(",", Offered(sockets, new[] { 11 })) + ")");
        Check(Offered(sockets, new[] { 1 }, 2).SequenceEqual(new int[0]), "positions the terminal doesn't draw are never offered");

        Check(Target(sockets, "Small", 0, new int[0]) == 1, "1x1 picked on empty 2x1 #0 goes in its first half #1");
        Check(Target(sockets, "Small", 0, new[] { 1 }) == 2, "1x1 picked on #0 with #1 full goes in #2");
        Check(Target(sockets, "Medium", 1, new[] { 1 }) == 0, "2x1 picked on 1x1 #1 takes the whole hardpoint #0");
        Check(Target(sockets, "Medium", 0, new int[0]) == 0, "2x1 picked on 2x1 #0 goes in #0");
        Check(Target(sockets, "Large", 0, new int[0]) == -1, "2x2 module does not fit a 2x1 hardpoint");
        Check(Target(sockets, "Medium", 7, new int[0]) == 11, "2x1 picked on #7 uses #11");
        Check(Target(sockets, "Large", 9, new[] { 11 }) == 6, "2x2 picked on #9 uses #6");
        Check(Displaced(sockets, 0, new[] { 1, 2, 3 }).SequenceEqual(new[] { 1, 2 }), "a 2x1 in #0 replaces both halves #1 and #2 but not #3");
        Check(Displaced(sockets, 6, new[] { 11, 9, 12 }).SequenceEqual(new[] { 11, 9 }), "a 2x2 in #6 replaces #11 and #9 but not #12");

        Console.WriteLine("Saved Custom loadout format:");
        Type config = plugin.GetType("VoidCrewShipCustomizer.HullConfig", true);
        object original = Activator.CreateInstance(config);
        var slots = (IDictionary)config.GetField("Slots").GetValue(original);
        slots[3] = "w:0123456789abcdef0123456789abcdef";
        slots[0] = "m:fedcba9876543210fedcba9876543210";
        var storage = (IDictionary)config.GetField("Storage").GetValue(original);
        storage[12] = new List<string> { "i:00112233445566778899aabbccddeeff", "", "c:0123456789abcdef0123456789abcdef" };
        config.GetField("Homunculus").SetValue(original, "i:ffeeddccbbaa99887766554433221100");
        object copy = config.GetMethod("Clone").Invoke(original, null);
        var copiedSlots = (IDictionary)config.GetField("Slots").GetValue(copy);
        var copiedStorage = (IDictionary)config.GetField("Storage").GetValue(copy);
        Check(copiedSlots.Count == 2 && (string)copiedSlots[3] == "w:0123456789abcdef0123456789abcdef", "hardpoints survive a save and load");
        Check(copiedStorage.Count == 1 && ((List<string>)copiedStorage[12])[2] == "c:0123456789abcdef0123456789abcdef" && ((List<string>)copiedStorage[12])[1] == "", "storage racks survive a save and load");
        Check((string)config.GetField("Homunculus").GetValue(copy) == "i:ffeeddccbbaa99887766554433221100", "homunculus survives a save and load");
        Check((int)config.GetProperty("StoredCount").GetValue(copy, null) == 2, "stored item count ignores empty slots");

        // Ship 2's own layouts (Void Crew Fleet) are saved next to ship 1's; files from 0.3.0 have none.
        Type json = System.Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        MethodInfo serialize = json.GetMethod("SerializeObject", new[] { typeof(object) });
        MethodInfo deserialize = json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
        Type store = plugin.GetType("VoidCrewShipCustomizer.LoadoutStore+StoreFile", true);
        object file = Activator.CreateInstance(store, true);
        ((IDictionary)store.GetField("Ships").GetValue(file))["4bc2ff9e1d156c94a9c94286a7aaa79b"] = original;
        ((IDictionary)store.GetField("SecondShips").GetValue(file))["4bc2ff9e1d156c94a9c94286a7aaa79b"] = copy;
        object reloaded = deserialize.Invoke(null, new object[] { (string)serialize.Invoke(null, new[] { file }), store });
        var second = (IDictionary)store.GetField("SecondShips").GetValue(reloaded);
        Check(second.Count == 1 && ((IDictionary)config.GetField("Slots").GetValue(second["4bc2ff9e1d156c94a9c94286a7aaa79b"])).Count == 2,
            "ship 2's own layouts survive a save and load");
        object old = deserialize.Invoke(null, new object[] { "{\"Version\":1,\"Ships\":{},\"CustomRuns\":[]}", store });
        Check(store.GetField("SecondShips").GetValue(old) is IDictionary, "a 0.3.0 save file loads, with no ship 2 layouts");

        // 0.5.0: the fabricator's starting alloys and level. Older files (and older versions on other PCs) leave them out.
        Check((int)config.GetField("Alloys").GetValue(copy) == -1 && (int)config.GetField("FabricatorLevel").GetValue(copy) == 0,
            "a new CUSTOM loadout starts with the game's own alloys and a MK I fabricator");
        config.GetField("Alloys").SetValue(original, 750);
        config.GetField("FabricatorLevel").SetValue(original, 2);
        object fabricatorCopy = config.GetMethod("Clone").Invoke(original, null);
        Check((int)config.GetField("Alloys").GetValue(fabricatorCopy) == 750 && (int)config.GetField("FabricatorLevel").GetValue(fabricatorCopy) == 2,
            "starting alloys and fabricator level survive a save and load");
        object older = deserialize.Invoke(null, new object[] { "{\"Slots\":{\"3\":\"w:0123456789abcdef0123456789abcdef\"},\"Storage\":{},\"Homunculus\":\"\"}", config });
        Check((int)config.GetField("Alloys").GetValue(older) == -1 && (int)config.GetField("FabricatorLevel").GetValue(older) == 0,
            "a 0.4.0 CUSTOM loadout loads with the game's own alloys and a MK I fabricator");
        config.GetField("Alloys").SetValue(older, 5000000);
        config.GetField("FabricatorLevel").SetValue(older, 9);
        config.GetMethod("Normalized").Invoke(older, null);
        Check((int)config.GetField("Alloys").GetValue(older) == 100000 && (int)config.GetField("FabricatorLevel").GetValue(older) == 2,
            "alloys and fabricator level are kept in range (at most 100000 and MK III)");

        // 0.6.0: saved loadouts (numbered slots per ship type, in saved-loadouts.json).
        Type savedFile = plugin.GetType("VoidCrewShipCustomizer.SavedLoadouts+SavedFile", true);
        Type saved = plugin.GetType("VoidCrewShipCustomizer.SavedLoadouts+Saved", true);
        string savedJson = "{\"Version\":1,\"Ships\":{\"4bc2ff9e1d156c94a9c94286a7aaa79b\":[" +
                           "{\"Slot\":0,\"Config\":{}},{\"Slot\":3,\"SavedAt\":\"first\",\"Config\":{}},{\"Slot\":3,\"SavedAt\":\"second\",\"Config\":{\"Alloys\":500}}," +
                           "{\"Slot\":11,\"Config\":{}},{\"Slot\":5},null]," +
                           "\"ac98ae5eb3b940747b0fbb7bd616b019\":null}}";
        object loadedSaves = plugin.GetType("VoidCrewShipCustomizer.SavedLoadouts", true).GetMethod("Normalize")
            .Invoke(null, new[] { deserialize.Invoke(null, new object[] { savedJson, savedFile }) });
        var savedShips = (IDictionary)savedFile.GetField("Ships").GetValue(loadedSaves);
        var destroyerSaves = (IList)savedShips["4bc2ff9e1d156c94a9c94286a7aaa79b"];
        Check(destroyerSaves.Count == 1 && (int)saved.GetField("Slot").GetValue(destroyerSaves[0]) == 3 && (string)saved.GetField("SavedAt").GetValue(destroyerSaves[0]) == "second",
            "saved loadouts keep slots 1 to 10 with a loadout, the last one for a slot");
        Check((int)config.GetField("Alloys").GetValue(saved.GetField("Config").GetValue(destroyerSaves[0])) == 500 &&
              ((IList)savedShips["ac98ae5eb3b940747b0fbb7bd616b019"]).Count == 0,
            "a saved loadout keeps its contents; a ship with nothing saved has an empty list");
        object savedAgain = deserialize.Invoke(null, new object[] { (string)serialize.Invoke(null, new[] { loadedSaves }), savedFile });
        Check(((IList)((IDictionary)savedFile.GetField("Ships").GetValue(savedAgain))["4bc2ff9e1d156c94a9c94286a7aaa79b"]).Count == 1, "saved loadouts survive a save and load");

        // 0.5.0: other players' changes are saved on the host's PC, so only what the ship has is kept.
        Console.WriteLine("Changes from other players:");
        Type hullType = plugin.GetType("VoidCrewShipCustomizer.HullInfo", true);
        Type shelfType = plugin.GetType("VoidCrewShipCustomizer.ShelfInfo", true);
        object hullInfo = Activator.CreateInstance(hullType);
        IList hullSockets = (IList)hullType.GetField("Sockets").GetValue(hullInfo);
        foreach (object socket in sockets) hullSockets.Add(socket);
        object shelf = Activator.CreateInstance(shelfType);
        shelfType.GetField("CoreIndex").SetValue(shelf, 12);
        IList shelfSlots = (IList)shelfType.GetField("Slots").GetValue(shelf);
        shelfSlots.Add(null);
        shelfSlots.Add(null);
        ((IList)hullType.GetField("Shelves").GetValue(hullInfo)).Add(shelf);
        object edit = Activator.CreateInstance(config);
        var editSlots = (IDictionary)config.GetField("Slots").GetValue(edit);
        editSlots[3] = "w:0123456789abcdef0123456789abcdef";
        editSlots[13] = "m:fedcba9876543210fedcba9876543210";
        editSlots[-1] = "m:fedcba9876543210fedcba9876543210";
        var editStorage = (IDictionary)config.GetField("Storage").GetValue(edit);
        editStorage[12] = new List<string> { "i:00112233445566778899aabbccddeeff", "", "i:00112233445566778899aabbccddeeff" };
        editStorage[99] = new List<string> { "i:00112233445566778899aabbccddeeff" };
        config.GetField("Homunculus").SetValue(edit, new string('x', 200));
        config.GetField("Alloys").SetValue(edit, -50);
        config.GetField("FabricatorLevel").SetValue(edit, 1);
        // The catalog half (real modules and items) needs the game running; the shape half is checked here.
        object clean = plugin.GetType("VoidCrewShipCustomizer.CustomLoadouts", true).GetMethod("Shape", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { hullInfo, edit });
        var cleanSlots = (IDictionary)config.GetField("Slots").GetValue(clean);
        var cleanStorage = (IDictionary)config.GetField("Storage").GetValue(clean);
        Check(cleanSlots.Count == 1 && cleanSlots.Contains(3), "only hardpoint positions the ship has are kept (got " + cleanSlots.Count + ")");
        Check(cleanStorage.Count == 1 && ((List<string>)cleanStorage[12]).Count == 2, "only racks the ship has are kept, with no more items than slots");
        Check((string)config.GetField("Homunculus").GetValue(clean) == "", "an over-long code is dropped");
        Check((int)config.GetField("Alloys").GetValue(clean) == -1 && (int)config.GetField("FabricatorLevel").GetValue(clean) == 1, "alloys and fabricator level from another player are kept in range");

        Type sync = plugin.GetType("VoidCrewShipCustomizer.RoomSync", true);
        Check((int)sync.GetField("Protocol").GetValue(null) == 3, "players are tagged with protocol 3 (0.8.0: changes are merged part by part)");
        Type editType = plugin.GetType("VoidCrewShipCustomizer.RoomSync+RoomEdit", true);
        object roomEdit = Activator.CreateInstance(editType);
        editType.GetField("Room").SetValue(roomEdit, "a room");
        editType.GetField("N").SetValue(roomEdit, 7);
        editType.GetField("Ship").SetValue(roomEdit, 2);
        editType.GetField("Config").SetValue(roomEdit, original);
        object arrived = deserialize.Invoke(null, new object[] { (string)serialize.Invoke(null, new[] { roomEdit }), editType });
        Check((string)editType.GetField("Room").GetValue(arrived) == "a room" && (int)editType.GetField("N").GetValue(arrived) == 7 && (int)editType.GetField("Ship").GetValue(arrived) == 2 &&
              (int)config.GetField("Alloys").GetValue(editType.GetField("Config").GetValue(arrived)) == 750,
            "a change sent to the host arrives whole (room, number, ship, loadout)");

        // CUSTOM GUIDs: the same per ship for every player, and the XOR undoes itself (TryGetDefOfCustomGuid relies on it).
        Console.WriteLine("CUSTOM loadout ids:");
        Type guid = System.Type.GetType("GUIDUnion, ResourceAssets", true);
        MethodInfo customGuidFor = plugin.GetType("VoidCrewShipCustomizer.CustomLoadouts", true).GetMethod("CustomGuidFor", BindingFlags.NonPublic | BindingFlags.Static);
        var known = new[]
        {
            new[] { "METEM Frigate", "ac98ae5eb3b940747b0fbb7bd616b019", "991c3a5b87ec75318fdb2fde8233a20b" },
            new[] { "METEM Striker", "d872500325a2d54498189cdfd4b788e7", "edf6c40611f7e0016ccc087a80929af5" },
            new[] { "METEM Destroyer", "4bc2ff9e1d156c94a9c94286a7aaa79b", "7e466b9b294059d15d1dd623f38fb589" }
        };
        foreach (string[] ship in known)
        {
            object hull = Activator.CreateInstance(guid, ship[1]);
            object custom = customGuidFor.Invoke(null, new[] { hull });
            object back = customGuidFor.Invoke(null, new[] { custom });
            Check(custom.ToString() == ship[2] && back.ToString() == ship[1], ship[0] + " CUSTOM id is " + ship[2] + " and maps back to the ship (got " + custom + ")");
        }

        // The room properties holding the host's shared CUSTOM loadouts: named per player ship and ship type.
        Console.WriteLine("Shared CUSTOM loadouts in the room:");
        object destroyer = Activator.CreateInstance(guid, "4bc2ff9e1d156c94a9c94286a7aaa79b");
        string key = (string)sync.GetMethod("LayoutKey").Invoke(null, new[] { (object)2, destroyer });
        Check(key == "SC_Layout2_4bc2ff9e1d156c94a9c94286a7aaa79b", "ship 2's Destroyer loadout is the room property " + key);
        MethodInfo parse = sync.GetMethod("TryParseLayoutKey", BindingFlags.NonPublic | BindingFlags.Static);
        var parsed = new object[] { key, null, null };
        Check((bool)parse.Invoke(null, parsed) && (int)parsed[1] == 2 && parsed[2].ToString() == "4bc2ff9e1d156c94a9c94286a7aaa79b", "that name reads back as ship 2, Destroyer");
        foreach (string other in new[] { "SC_Layout3_4bc2ff9e1d156c94a9c94286a7aaa79b", "SC_Layout1", "SC_Layout1_", "SC_Custom" })
        {
            Check(!(bool)parse.Invoke(null, new object[] { other, null, null }), "\"" + other + "\" is not taken for a shared loadout");
        }

        // Ship Customizer on its own: no Void Crew Fleet, so nothing for a second ship and no team rules.
        Console.WriteLine("Without Void Crew Fleet:");
        Type log = System.Type.GetType("BepInEx.Logging.ManualLogSource, BepInEx", true);
        plugin.GetType("VoidCrewShipCustomizer.Log", true).GetMethod("Init").Invoke(null, new[] { Activator.CreateInstance(log, "offline checks") });
        Type fleet = plugin.GetType("VoidCrewShipCustomizer.FleetLink", true);
        Check(!(bool)fleet.GetProperty("Running").GetValue(null, null), "Fleet counts as not running");
        Check((bool)fleet.GetMethod("CanEditShip").Invoke(null, new object[] { 5, 1 }) && (bool)fleet.GetMethod("CanEditShip").Invoke(null, new object[] { 5, 2 }),
            "every player may change every ship (no team rules)");
        Check(fleet.GetMethod("TeamNameOfShip").Invoke(null, new object[] { 2 }) == null && fleet.GetMethod("ShipColor").Invoke(null, new object[] { 2 }) == null,
            "no team names or colours");
        Type api = plugin.GetType("VoidCrewShipCustomizer.ShipCustomizerApi", true);
        api.GetMethod("SetTerminalShip").Invoke(null, new object[] { 2 });
        Check((int)plugin.GetType("VoidCrewShipCustomizer.CustomLoadouts", true).GetField("TerminalShip").GetValue(null) == 1, "the terminal stays on ship 1 when asked for ship 2");
        object destroyerCustom = Activator.CreateInstance(guid, "7e466b9b294059d15d1dd623f38fb589");
        var refusal = (string)api.GetMethod("CheckShip2").Invoke(null, new[] { destroyerCustom, (object)1 });
        Check(refusal != null && refusal.Contains("Fleet"), "ship 2 can't be given a CUSTOM loadout (\"" + refusal + "\")");
        Check(api.GetMethod("BuildShip2").Invoke(null, new[] { destroyerCustom }) == null, "no ship 2 is built");
        Check((int)api.GetMethod("GetShip2Alloys").Invoke(null, new[] { destroyerCustom }) == -1, "ship 2 has no starting alloys of its own");
        Check(!(bool)fleet.GetProperty("ReadsShip2Alloys").GetValue(null, null), "the FABRICATOR menu doesn't offer ship 2's alloys");
        Check(!(bool)fleet.GetProperty("ShipRulesActive").GetValue(null, null) && (bool)fleet.GetMethod("IsTierAllowed").Invoke(null, new object[] { 3 }) &&
              (bool)fleet.GetMethod("IsCategoryAllowed").Invoke(null, new object[] { "WEAPONS" }) && (bool)fleet.GetMethod("IsItemAllowed").Invoke(null, new object[] { "w:0123456789abcdef0123456789abcdef" }) &&
              (int)fleet.GetProperty("FabricatorMaxLevel").GetValue(null, null) == 2 && (int)fleet.GetProperty("MaxStartAlloys").GetValue(null, null) == int.MaxValue,
            "no match rules: every tier, category and item, MK III and any alloys are allowed");
        var ruleOptions = (string[])api.GetMethod("GetRuleOptions").Invoke(null, null);
        Check(ruleOptions != null && ruleOptions.Length == 0, "GetRuleOptions answers with an empty list before the hub has loaded");

        MatchRuleChecks(plugin, config, hullInfo);
        SharedEditingChecks(plugin, config, sockets);
        ShareCodeChecks(plugin, config);
        SaveFileChecks(plugin, config);
    }

    // ------------------------------------------------------------------ 0.7.0: Fleet Battle match rules, blessings

    private static void MatchRuleChecks(Assembly plugin, Type config, object hullInfo)
    {
        Console.WriteLine("Fleet Battle match rules:");
        Type rules = plugin.GetType("VoidCrewShipCustomizer.MatchRules", true);
        Type optionType = plugin.GetType("VoidCrewShipCustomizer.Option", true);
        Type kind = plugin.GetType("VoidCrewShipCustomizer.OptionKind", true);
        Type rarity = System.Type.GetType("CG.Ship.Object.RarityType, Assembly-CSharp", true);
        Func<string, string, string, string, string, object> option = (code, optionKind, name, rarityName, ruleCategory) =>
        {
            object o = Activator.CreateInstance(optionType);
            optionType.GetField("Code").SetValue(o, code);
            optionType.GetField("Kind").SetValue(o, Enum.Parse(kind, optionKind));
            optionType.GetField("Name").SetValue(o, name);
            optionType.GetField("Rarity").SetValue(o, Enum.Parse(rarity, rarityName));
            optionType.GetField("RuleCategory").SetValue(o, ruleCategory);
            return o;
        };
        MethodInfo tierOf = rules.GetMethod("TierOf");
        MethodInfo alwaysBanned = rules.GetMethod("AlwaysBanned");
        object arcShield3 = option("m:9c90e05e9d35b07448983ce63547616a", "Module", "Arc Shield MkIII", "Epic", "DEFENSE");
        object lance2 = option("w:0123456789abcdef0123456789abcdef", "Weapon", "<b>B.R.A.I.N.</b>: Absolver Lance MkII", "Rare", "WEAPONS");
        object point1 = option("m:1828a943bdb98d84f96415a6130120fd", "Module", "Kinetic Point Defense MkI", "Common", "DEFENSE");
        object crate = option("i:6e877538ce61f0a42a31e75d800171e4", "Item", "Heavy Caliber: Medium Crate", "Rare", "AMMO & POWER");
        object relic = option("i:6810d852c8fc354458dd401c57fde7a1", "Item", "Astral Gravity Well", "Legendary", "RELICS");
        object placeholder = option("w:224467f966b840d458b207ffe12a6f1f", "Weapon", "Placeholder", "None", "WEAPONS");
        object placeholderCrate = option("c:180af8388bfe78641aac04e8bf9f2300", "WeaponCrate", "Placeholder", "Common", "WEAPON CRATES");
        object newPlaceholder = option("w:00000000000000000000000000000001", "Weapon", "Placeholder", "Epic", "WEAPONS");
        object fabricator = option("m:e6922b2332bafa64a8872faa181d6fc6", "Module", "Fabricator", "None", "SUPPORT MODULES");
        object armoryKey = option("i:94d959e4b33888a4dafaa5f054d7c492", "Item", "Armory Exchange Key", "Special", "SPECIAL");
        object relicShrine = option("i:59d0f5d46d3660b4a984ec03824d38a4", "Item", "Relic Shrine", "Epic", "ANIMUS CRATES");
        Check((int)tierOf.Invoke(null, new[] { arcShield3 }) == 3 && (int)tierOf.Invoke(null, new[] { lance2 }) == 2 && (int)tierOf.Invoke(null, new[] { point1 }) == 1,
            "the MK in a name gives the tier (MkIII 3, MkII 2, MkI 1), tags and all");
        object highDamage = option("i:c85737fb51b82aa499eb24304bcc0ffd", "Item", "High Damage", "Legendary", "WEAPON MODS");
        object oxygen = option("i:6c37b5363f7ef7844881a301dca76572", "Item", "Oxygen Tank", "Special", "SUPPORT ITEMS");
        Check((int)tierOf.Invoke(null, new[] { crate }) == 2 && (int)tierOf.Invoke(null, new[] { highDamage }) == 3,
            "without a MK the rarity gives it: Rare 2, and Legendary counts as the top tier, 3 (a MK I only match leaves out High Damage)");
        Check((int)tierOf.Invoke(null, new[] { oxygen }) == 0 && (int)tierOf.Invoke(null, new[] { placeholder }) == 0,
            "Special items and no rarity are tier 0, which no tier rule refuses");
        Check(new[] { relic, placeholder, placeholderCrate, newPlaceholder, fabricator, armoryKey }.All(o => alwaysBanned.Invoke(null, new[] { o }) != null),
            "never in a Fleet Battle: relics, Placeholder weapons and their crates (by id or name), the Fabricator modules, the Armory Exchange Key");
        Check(new[] { arcShield3, lance2, point1, crate, relicShrine }.All(o => alwaysBanned.Invoke(null, new[] { o }) == null),
            "everything else may be (the Relic Shrine animus crate isn't a relic)");
        var categories = (string[])rules.GetField("Categories").GetValue(null);
        Check(categories.Length == 12 && !categories.Contains("RELICS") && categories[2] == "SUPPORT MODULES" && categories[4] == "SUPPORT ITEMS" && categories[11] == "HOMUNCULUS",
            "the rules' 12 categories, without RELICS, as agreed with Void Crew Fleet");

        // Outside a Fleet Battle (and without Void Crew Fleet) nothing is refused, hidden or taken off.
        Check(rules.GetMethod("WhyRefused").Invoke(null, new[] { relic }) == null && !(bool)rules.GetMethod("Hidden").Invoke(null, new[] { placeholder }),
            "outside a Fleet Battle relics and Placeholders are offered as before");
        object loadout = Activator.CreateInstance(config);
        config.GetField("Alloys").SetValue(loadout, 5000);
        config.GetField("FabricatorLevel").SetValue(loadout, 2);
        Check(((IList)rules.GetMethod("Check").Invoke(null, new[] { hullInfo, loadout, (object)true })).Count == 0 && (int)config.GetField("Alloys").GetValue(loadout) == 5000,
            "outside a Fleet Battle a CUSTOM loadout breaks no rules and nothing is taken off");
        Check((int)rules.GetMethod("AllowedAlloys").Invoke(null, new object[] { 5000 }) == 5000 && (int)rules.GetMethod("AllowedFabricatorLevel").Invoke(null, new object[] { 2 }) == 2,
            "without match rules, any alloys and MK III");

        // Fleet's limits, as the rules apply them.
        MethodInfo clamp = rules.GetMethod("ClampAlloys", BindingFlags.NonPublic | BindingFlags.Static);
        Func<int, int, int> alloys = (amount, max) => (int)clamp.Invoke(null, new object[] { amount, max });
        Check(alloys(5000, -1) == -1 && alloys(5000, 500) == 500 && alloys(300, 500) == 300 && alloys(5000, int.MaxValue) == 5000 && alloys(-1, 500) == -1 && alloys(100, 0) == 0,
            "starting alloys: -1 allows only the game's own, a cap lowers what is over it, int.MaxValue allows anything");
        MethodInfo highest = rules.GetMethod("HighestLevel", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo levelAllowed = rules.GetMethod("LevelAllowed", BindingFlags.NonPublic | BindingFlags.Static);
        Func<int, int, int[], int> level = (wanted, max, tiers) => (int)highest.Invoke(null, new object[] { wanted, max, new Func<int, bool>(tiers.Contains) });
        Check(level(2, 0, new[] { 1 }) == 0 && level(2, 2, new[] { 1, 2, 3 }) == 2 && level(2, 1, new[] { 1, 2 }) == 1,
            "fabricator: MK I only, every tier, and up to MK II");
        Check(level(2, 2, new[] { 1, 3 }) == 2 && level(1, 2, new[] { 1, 3 }) == 0,
            "fabricator with a gap (MK I and MK III allowed, not MK II): MK III stays, MK II comes down to MK I");
        Check((bool)levelAllowed.Invoke(null, new object[] { 0, 2, new Func<int, bool>(new[] { 2, 3 }.Contains) }),
            "MK I is always allowed, even when only higher tiers are (every run starts at MK I)");

        // The message telling every player what the rules took off.
        Type sync = plugin.GetType("VoidCrewShipCustomizer.RoomSync", true);
        Check((string)sync.GetField("NoticeKey").GetValue(null) == "SC_Notice", "the host's message is the room property SC_Notice");
        Type json = System.Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        Type noticeType = plugin.GetType("VoidCrewShipCustomizer.RoomSync+RoomNotice", true);
        object notice = Activator.CreateInstance(noticeType);
        noticeType.GetField("N").SetValue(notice, 3);
        noticeType.GetField("T").SetValue(notice, -2000000000);
        noticeType.GetField("Text").SetValue(notice, "Match rules: line one\nline two");
        object noticeBack = json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })
            .Invoke(null, new object[] { (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { notice }), noticeType });
        Check((int)noticeType.GetField("N").GetValue(noticeBack) == 3 && (int)noticeType.GetField("T").GetValue(noticeBack) == -2000000000 &&
              (string)noticeType.GetField("Text").GetValue(noticeBack) == "Match rules: line one\nline two",
            "the message arrives whole (number, time, lines)");

        // The game's descriptions with their tags: plain names, and a Blessed Homunculus's blessing.
        Console.WriteLine("Blessings and plain names:");
        Type richText = plugin.GetType("VoidCrewShipCustomizer.RichText", true);
        MethodInfo plain = richText.GetMethod("Plain");
        MethodInfo lastSection = richText.GetMethod("LastSection");
        Check((string)plain.Invoke(null, new object[] { "<b>B.R.A.I.N.</b>: Auto-Mechanic MkI" }) == "B.R.A.I.N.: Auto-Mechanic MkI", "tags are taken out of names");
        const string Effects = "Improved <color=#FFD200>Damage</color> and <color=#FFD200>Range</color> for <color=#FFD200>Benediction</color> Weapons.\n" +
                               "Reduced <color=#FFD200>Fire Rate</color> for <color=#FFD200>Benediction</color> Weapons.";
        const string Intro = "Place in the <color=#FFD200>Central Computer Cradle</color> to allow <color=#FFD200>Ship Power-on</color>.\n<i>The Voice of METEM.</i>";
        Func<string, string> blessing = body => (string)lastSection.Invoke(null, new object[] { body });
        Check(blessing(Intro + "\n\n<b>Blessing</b>\n" + Effects) == Effects, "a blessing is the lines under the bold Blessing heading, highlights kept");
        Check(blessing(Intro + "\r\n\r\n<color=#FFFFFF><b>Blessing</b></color>\r\n" + Effects.Replace("\n", "\r\n") + "\r\n") == Effects, "the same with a coloured heading and Windows line ends");
        Check(blessing(Intro + "\n\nBlessing\n" + Effects) == Effects, "a heading without bold: the last paragraph, without its heading");
        Check(blessing(Intro) == null && blessing("") == null, "the standard homunculus (one paragraph, no heading) has no blessing");

        // 0.7.1: item names on the storage-rack tiles get smaller text when they are long, so they fit under the icon.
        Console.WriteLine("Storage tiles:");
        MethodInfo nameSize = plugin.GetType("VoidCrewShipCustomizer.StoragePanel", true).GetMethod("NameFontSize", BindingFlags.NonPublic | BindingFlags.Static);
        Func<string, int> size = name => (int)nameSize.Invoke(null, new object[] { name });
        Check(size("Large Power Cell") == 18 && size("Large Nano Alloy Cluster") == 15 && size("Heavy Caliber: Large Crate") == 13 &&
              size("Payload Battery: Cluster Missiles") == 13 && size(null) == 18,
            "tile names: the game's small text up to 16 letters, 15 up to 24, 13 for longer ones");
    }

    // ------------------------------------------------------------------ 0.8.0: several players changing one loadout

    private static void SharedEditingChecks(Assembly plugin, Type config, IList sockets)
    {
        Console.WriteLine("Several players changing one loadout:");
        Type merge = plugin.GetType("VoidCrewShipCustomizer.LayoutMerge", true);
        var racks = new Dictionary<int, int> { { 12, 3 } };
        Func<object> layout = () =>
        {
            object c = Activator.CreateInstance(config);
            ((IDictionary)config.GetField("Slots").GetValue(c))[1] = "m:11111111111111111111111111111111";
            ((IDictionary)config.GetField("Storage").GetValue(c))[12] = new List<string> { "i:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "", "" };
            config.GetMethod("Normalized").Invoke(c, null);
            return c;
        };
        Func<object, object> clone = c => config.GetMethod("Clone").Invoke(c, null);
        Func<object, IDictionary> slotsOf = c => (IDictionary)config.GetField("Slots").GetValue(c);
        Func<object, List<string>> rackOf = c => (List<string>)((IDictionary)config.GetField("Storage").GetValue(c))[12];
        MethodInfo mergeMethod = merge.GetMethod("Merge");
        Func<object, object, object, List<string>, List<string>, object> run = (current, seen, wanted, applied, refused) =>
            mergeMethod.Invoke(null, new[] { current, seen, wanted, sockets, racks, applied, refused });

        // A and B both start from the same loadout. B's change (rack slot 2) reached the host first; A changed the
        // hardpoint. Both changes stay.
        object seen = layout();
        object current = clone(seen);
        rackOf(current)[1] = "i:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        object wanted = clone(seen);
        slotsOf(wanted)[1] = "m:22222222222222222222222222222222";
        var applied = new List<string>();
        var refused = new List<string>();
        object merged = run(current, seen, wanted, applied, refused);
        Check((string)slotsOf(merged)[1] == "m:22222222222222222222222222222222" && rackOf(merged)[1] == "i:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" && refused.Count == 0,
            "changes to different parts are both kept (A's hardpoint and B's rack slot)");

        // Both changed rack slot 1: B's came first, so A's is refused (and B's kept), not silently replaced.
        current = clone(seen);
        rackOf(current)[0] = "i:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        wanted = clone(seen);
        rackOf(wanted)[0] = "i:cccccccccccccccccccccccccccccccc";
        applied.Clear();
        refused.Clear();
        merged = run(current, seen, wanted, applied, refused);
        Check(rackOf(merged)[0] == "i:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" && refused.Count == 1 && refused[0] == "rack:12:0",
            "the same part changed by two players: the first change stays, the second is refused (got " + string.Join(",", refused) + ")");

        // The same change twice (a resent edit) changes nothing and refuses nothing.
        current = clone(seen);
        rackOf(current)[2] = "i:dddddddddddddddddddddddddddddddd";
        wanted = clone(current);
        applied.Clear();
        refused.Clear();
        merged = run(current, seen, wanted, applied, refused);
        Check(applied.Count == 0 && refused.Count == 0 && rackOf(merged)[2] == "i:dddddddddddddddddddddddddddddddd", "the same change again is no conflict");

        // A hardpoint is one part with its overlapping positions: a 2x1 in #0 replaces the 1x1 in #1 as a whole.
        current = clone(seen);
        wanted = clone(seen);
        slotsOf(wanted).Remove(1);
        slotsOf(wanted)[0] = "m:33333333333333333333333333333333";
        applied.Clear();
        refused.Clear();
        merged = run(current, seen, wanted, applied, refused);
        Check(slotsOf(merged).Count == 1 && (string)slotsOf(merged)[0] == "m:33333333333333333333333333333333" && applied.Count == 1 && applied[0] == "hp:0",
            "a hardpoint changes as a whole (the 2x1 in #0 takes the place of the 1x1 in #1)");

        // What a change carries arrives whole: the loadout it was made against, and the host's answer.
        Type json = System.Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        MethodInfo serialize = json.GetMethod("SerializeObject", new[] { typeof(object) });
        MethodInfo deserialize = json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
        Type editType = plugin.GetType("VoidCrewShipCustomizer.RoomSync+RoomEdit", true);
        object edit = Activator.CreateInstance(editType);
        editType.GetField("Config").SetValue(edit, wanted);
        editType.GetField("Base").SetValue(edit, seen);
        object arrived = deserialize.Invoke(null, new object[] { (string)serialize.Invoke(null, new[] { edit }), editType });
        Check(editType.GetField("Base").GetValue(arrived) != null && slotsOf(editType.GetField("Base").GetValue(arrived)).Contains(1),
            "a change carries the loadout it was made against");
        Type layoutType = plugin.GetType("VoidCrewShipCustomizer.RoomSync+RoomLayout", true);
        object answer = Activator.CreateInstance(layoutType);
        layoutType.GetField("Refused").SetValue(answer, new List<string> { "STORAGE RACK 1 slot 1 (Ana)" });
        object answered = deserialize.Invoke(null, new object[] { (string)serialize.Invoke(null, new[] { answer }), layoutType });
        var refusedBack = (List<string>)layoutType.GetField("Refused").GetValue(answered);
        Check(refusedBack != null && refusedBack.Count == 1 && refusedBack[0] == "STORAGE RACK 1 slot 1 (Ana)", "the host's answer says what was refused and who changed it first");
        object older = deserialize.Invoke(null, new object[] { "{\"Room\":\"r\",\"N\":2,\"Hull\":\"x\",\"Ship\":1,\"Config\":{}}", editType });
        Check(editType.GetField("Base").GetValue(older) == null, "a change from 0.7.0 or older has no Base (the host takes it whole, as before)");
    }

    // ------------------------------------------------------------------ 0.8.0: share codes

    private static void ShareCodeChecks(Assembly plugin, Type config)
    {
        Console.WriteLine("Share codes:");
        Type codes = plugin.GetType("VoidCrewShipCustomizer.ShareCodes", true);
        object loadout = Activator.CreateInstance(config);
        ((IDictionary)config.GetField("Slots").GetValue(loadout))[3] = "w:0123456789abcdef0123456789abcdef";
        ((IDictionary)config.GetField("Storage").GetValue(loadout))[12] = new List<string> { "i:00112233445566778899aabbccddeeff", "", "c:0123456789abcdef0123456789abcdef" };
        ((IDictionary)config.GetField("Contents").GetValue(loadout))[3] = new List<string> { "i:00112233445566778899aabbccddeeff", "" };
        config.GetField("Alloys").SetValue(loadout, 500);
        config.GetField("FabricatorLevel").SetValue(loadout, 2);
        var code = (string)codes.GetMethod("Make").Invoke(null, new[] { (object)"4bc2ff9e1d156c94a9c94286a7aaa79b", loadout });
        Check(code.StartsWith("SC1-") && code.Length < 1000 && code.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'),
            "a share code is one compressed, web-safe line (" + code.Length + " characters)");
        MethodInfo read = codes.GetMethod("TryRead");
        var args = new object[] { "Try my Destroyer! " + code + " good luck", null, null, null };
        bool ok = (bool)read.Invoke(null, args);
        object back = args[2];
        Check(ok && (string)args[1] == "4bc2ff9e1d156c94a9c94286a7aaa79b" && (int)config.GetField("Alloys").GetValue(back) == 500 &&
              ((List<string>)((IDictionary)config.GetField("Storage").GetValue(back))[12])[2] == "c:0123456789abcdef0123456789abcdef" &&
              ((List<string>)((IDictionary)config.GetField("Contents").GetValue(back))[3])[0] == "i:00112233445566778899aabbccddeeff",
            "a pasted message with a share code in it brings the whole loadout back (ship type, racks, contents, alloys)");
        var broken = new object[] { code.Substring(0, code.Length / 2), null, null, null };
        Check(!(bool)read.Invoke(null, broken) && broken[3] != null, "a cut-off code is refused with a reason (\"" + broken[3] + "\")");
        var none = new object[] { "nothing to see here", null, null, null };
        Check(!(bool)read.Invoke(null, none) && ((string)none[3]).Contains("SC1-"), "text without a code says how a code starts");
    }

    // ------------------------------------------------------------------ 0.8.0: the save files

    private static void SaveFileChecks(Assembly plugin, Type config)
    {
        Console.WriteLine("Save files:");
        object damaged = Activator.CreateInstance(config);
        ((IDictionary)config.GetField("Storage").GetValue(damaged))[3] = null;
        ((IDictionary)config.GetField("Slots").GetValue(damaged))[5] = null;
        ((IDictionary)config.GetField("Contents").GetValue(damaged))[7] = new List<string> { "i:00112233445566778899aabbccddeeff" };
        config.GetMethod("Normalized").Invoke(damaged, null);
        Check(((IList)((IDictionary)config.GetField("Storage").GetValue(damaged))[3]).Count == 0 && ((IDictionary)config.GetField("Slots").GetValue(damaged)).Count == 0 &&
              ((IDictionary)config.GetField("Contents").GetValue(damaged)).Count == 0 && (int)config.GetProperty("StoredCount").GetValue(damaged, null) == 0,
            "a damaged loadout (a missing rack, an empty hardpoint, contents with no module) loads without errors");

        Type safe = plugin.GetType("VoidCrewShipCustomizer.SafeFile", true);
        string folder = Path.Combine(Path.GetTempPath(), "ShipCustomizerCheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "loadouts.json");
            MethodInfo write = safe.GetMethod("Write");
            MethodInfo read = safe.GetMethod("Read");
            write.Invoke(null, new object[] { file, "first" });
            write.Invoke(null, new object[] { file, "second" });
            var args = new object[] { file, null };
            read.Invoke(null, args);
            Check((string)args[1] == "second" && File.ReadAllText(file + ".bak") == "first" && !File.Exists(file + ".tmp"),
                "a save replaces the file in one step and keeps the one before as .bak");
            File.Delete(file);
            File.WriteAllText(file + ".tmp", "cut short");
            args = new object[] { file, null };
            read.Invoke(null, args);
            Check((string)args[1] == "cut short", "a save an older version cut short (only .tmp left) is still found");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static void AddSocket(IList sockets, Type vector3, object right, object forward, string size, float x, float y, float z)
    {
        object socket = Activator.CreateInstance(_socketType);
        object buildSize = Enum.Parse(_buildSize, size);
        object position = Activator.CreateInstance(vector3, x, y, z);
        object cells = _hardpoints.GetMethod("CellsFor").Invoke(null, new[] { buildSize, position, right, forward });
        _socketType.GetField("Index").SetValue(socket, sockets.Count);
        _socketType.GetField("Size").SetValue(socket, buildSize);
        _socketType.GetField("Cells").SetValue(socket, cells);
        sockets.Add(socket);
    }

    private static int[] Overlaps(IList sockets, int index)
    {
        return ((List<int>)_socketType.GetField("Overlaps").GetValue(sockets[index])).OrderBy(i => i).ToArray();
    }

    private static int[] Offered(IList sockets, int[] fitted, int drawn = 100)
    {
        var result = (HashSet<int>)_hardpoints.GetMethod("OfferedEmpty").Invoke(null, new object[] { sockets, new List<int>(fitted), drawn });
        return result.OrderBy(i => i).ToArray();
    }

    private static int Target(IList sockets, string size, int clicked, int[] fitted)
    {
        object target = _hardpoints.GetMethod("TargetFor").Invoke(null, new[] { Enum.Parse(_buildSize, size), sockets[clicked], sockets, new List<int>(fitted), 100 });
        return target == null ? -1 : (int)_socketType.GetField("Index").GetValue(target);
    }

    private static int[] Displaced(IList sockets, int target, int[] fitted)
    {
        return ((List<int>)_hardpoints.GetMethod("Displaced").Invoke(null, new[] { sockets[target], new List<int>(fitted) })).ToArray();
    }
}
