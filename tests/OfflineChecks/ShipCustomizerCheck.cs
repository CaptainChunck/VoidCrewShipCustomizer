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
//  3. a Custom loadout config survives a JSON round trip with the game's Newtonsoft.
// Run it with tests\build-and-test.ps1, which builds the plugin first. When the plugin starts looking up another game
// member by name (a Harmony patch target, a ___field injection, AccessTools.Field/Method), add a check for it below.
// Usage: ShipCustomizerCheck.exe <plugin dir> <game Managed dir> <BepInEx core dir> [<desktop Newtonsoft.Json dir>]
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
