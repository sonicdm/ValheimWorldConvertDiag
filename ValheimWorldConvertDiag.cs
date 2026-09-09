using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ValheimWorldConvertDiag
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class ValheimWorldConvertDiagPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "sonicdm.valheimworldconvertdiag";
        public const string PluginName = "Valheim World Convert Diagnostic";
        public const string PluginVersion = "1.2.0";

        internal static ManualLogSource Log;
        internal static readonly object Sync = new object();
        internal static bool InWorldConversion;
        internal static string CurrentInventory = "<none>";
        internal static string CurrentZdo = "<none>";
        internal static string InventorySourceZdo = "<none>";
        internal static string LastNamedItem = "<none>";
        internal static string LastTempItem = "<none>";
        internal static string LastComparison = "<none>";
        internal static readonly Queue<string> RecentNamedItems = new Queue<string>();
        internal static string DiagnosticFile;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            DiagnosticFile = GetDiagnosticPath();
            Write("============================================================");
            Write($"{PluginName} {PluginVersion} loading");
            Write($"Diagnostic file: {DiagnosticFile}");

            try
            {
                _harmony = new Harmony(PluginGuid);
                PatchRequiredMethods(_harmony);
                Write("Diagnostic patches installed successfully.");
                Write("Waiting for ZDOMan.ConvertInventories(). No world data is modified by this plugin.");
            }
            catch (Exception ex)
            {
                Write("FATAL: Failed to install diagnostic patches:\n" + ex);
                throw;
            }
        }

        private void OnDestroy()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch
            {
                // Diagnostic plugin only. Never interfere with shutdown.
            }
        }

        private static void PatchRequiredMethods(Harmony harmony)
        {
            Type zdoMan = FindType("ZDOMan");
            Type inventory = FindType("Inventory");
            Type itemData = FindType("ItemDrop+ItemData");
            Type zdo = FindType("ZDO");

            if (zdoMan == null) throw new TypeLoadException("Could not find Valheim type ZDOMan");
            if (inventory == null) throw new TypeLoadException("Could not find Valheim type Inventory");
            if (itemData == null) throw new TypeLoadException("Could not find Valheim type ItemDrop+ItemData");
            if (zdo == null) throw new TypeLoadException("Could not find Valheim type ZDO");

            MethodInfo convertInventories = zdoMan.GetMethods(AllMethods)
                .FirstOrDefault(m => m.Name == "ConvertInventories");
            if (convertInventories == null)
                throw new MissingMethodException("Could not find ZDOMan.ConvertInventories");

            MethodInfo loadOld = inventory.GetMethods(AllMethods)
                .FirstOrDefault(m => m.Name == "LoadOld");
            if (loadOld == null)
                throw new MissingMethodException("Could not find Inventory.LoadOld");

            var addItemStringMethods = inventory.GetMethods(AllMethods)
                .Where(m => m.Name == "AddItem")
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length > 0 && p[0].ParameterType == typeof(string);
                })
                .ToList();

            if (addItemStringMethods.Count == 0)
                throw new MissingMethodException("Could not find Inventory.AddItem(string, ...)");

            var addTempItemMethods = inventory.GetMethods(AllMethods)
                .Where(m => m.Name == "AddTempItem")
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length > 0 && p[0].ParameterType == typeof(int);
                })
                .ToList();

            if (addTempItemMethods.Count == 0)
                throw new MissingMethodException("Could not find Inventory.AddTempItem(int, ...)");

            var isSameTypeMethods = itemData.GetMethods(AllMethods)
                .Where(m => m.Name == "IsSameType")
                .ToList();

            if (isSameTypeMethods.Count == 0)
                throw new MissingMethodException("Could not find ItemDrop.ItemData.IsSameType");

            var zdoInventoryGetterMethods = zdo.GetMethods(AllMethods)
                .Where(m => (m.Name == "GetString" || m.Name == "GetByteArray" || m.Name == "GetBytes"))
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length > 0 && (p[0].ParameterType == typeof(string) || p[0].ParameterType == typeof(int));
                })
                .ToList();

            harmony.Patch(
                convertInventories,
                prefix: new HarmonyMethod(typeof(ValheimWorldConvertDiagPlugin), nameof(ConvertInventoriesPrefix)),
                finalizer: new HarmonyMethod(typeof(ValheimWorldConvertDiagPlugin), nameof(ConvertInventoriesFinalizer)));

            harmony.Patch(
                loadOld,
                prefix: new HarmonyMethod(typeof(ValheimWorldConvertDiagPlugin), nameof(InventoryLoadOldPrefix)),
                finalizer: new HarmonyMethod(typeof(ValheimWorldConvertDiagPlugin), nameof(InventoryLoadOldFinalizer)));

            foreach (MethodInfo method in addItemStringMethods)
            {
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(ValheimWorldConvertDiagPlugin), nameof(AddItemStringPrefix)));
            }

            foreach (MethodInfo method in addTempItemMethods)
            {
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(ValheimWorldConvertDiagPlugin), nameof(AddTempItemPrefix)));
            }

            foreach (MethodInfo method in isSameTypeMethods)
            {
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(ValheimWorldConvertDiagPlugin), nameof(IsSameTypePrefix)));
            }

            foreach (MethodInfo method in zdoInventoryGetterMethods)
            {
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(ValheimWorldConvertDiagPlugin), nameof(ZdoInventoryGetterPrefix)));
            }

            Write($"Patched: ConvertInventories=1, LoadOld=1, AddItem(string)={addItemStringMethods.Count}, AddTempItem={addTempItemMethods.Count}, IsSameType={isSameTypeMethods.Count}, ZDO inventory getters={zdoInventoryGetterMethods.Count}");
        }

        public static void ConvertInventoriesPrefix(MethodBase __originalMethod, object[] __args)
        {
            SafeDiagnostic(() =>
            {
                lock (Sync)
                {
                    InWorldConversion = true;
                    CurrentInventory = "<not started>";
                    CurrentZdo = "<none yet>";
                    InventorySourceZdo = "<none yet>";
                    LastNamedItem = "<none>";
                    LastTempItem = "<none>";
                    LastComparison = "<none>";
                    RecentNamedItems.Clear();
                }

                Write("=== LEGACY WORLD INVENTORY CONVERSION STARTED ===");
                Write("ConvertInventories args: " + FormatArguments(__originalMethod, __args));
            });
        }

        public static Exception ConvertInventoriesFinalizer(Exception __exception)
        {
            SafeDiagnostic(() =>
            {
                if (__exception != null)
                {
                    Write("=== ConvertInventories EXITED WITH EXCEPTION ===");
                    WriteFailureReport(__exception, null);
                }
                else
                {
                    Write("=== LEGACY WORLD INVENTORY CONVERSION COMPLETED ===");
                }

                lock (Sync)
                {
                    InWorldConversion = false;
                }
            });

            return __exception;
        }

        public static void InventoryLoadOldPrefix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            if (!InWorldConversion) return;

            SafeDiagnostic(() =>
            {
                lock (Sync)
                {
                    CurrentInventory = DescribeInventory(__instance);
                    InventorySourceZdo = CurrentZdo;
                    LastNamedItem = "<none in this inventory yet>";
                    LastTempItem = "<none in this inventory yet>";
                    LastComparison = "<none in this inventory yet>";
                    RecentNamedItems.Clear();
                }
            });
        }

        public static Exception InventoryLoadOldFinalizer(Exception __exception, object __instance)
        {
            if (!InWorldConversion || __exception == null) return __exception;

            SafeDiagnostic(() =>
            {
                Write("!!! Inventory.LoadOld FAILED !!!");
                WriteFailureReport(__exception, __instance);
            });

            return __exception;
        }

        public static void AddItemStringPrefix(MethodBase __originalMethod, object __instance, object[] __args)
        {
            if (!InWorldConversion) return;

            SafeDiagnostic(() =>
            {
                string entry = FormatArguments(__originalMethod, __args);
                lock (Sync)
                {
                    CurrentInventory = DescribeInventory(__instance);
                    LastNamedItem = entry;
                    RecentNamedItems.Enqueue(entry);
                    while (RecentNamedItems.Count > 12)
                        RecentNamedItems.Dequeue();
                }
            });
        }

        public static void AddTempItemPrefix(MethodBase __originalMethod, object __instance, object[] __args)
        {
            if (!InWorldConversion) return;

            SafeDiagnostic(() =>
            {
                lock (Sync)
                {
                    CurrentInventory = DescribeInventory(__instance);
                    LastTempItem = FormatArguments(__originalMethod, __args);
                }
            });
        }

        public static void ZdoInventoryGetterPrefix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            if (!InWorldConversion || __args == null || __args.Length == 0) return;

            SafeDiagnostic(() =>
            {
                object key = __args[0];
                bool isItemsKey = false;
                if (key is string s)
                    isItemsKey = string.Equals(s, "items", StringComparison.Ordinal);
                else if (key is int i)
                    isItemsKey = i == StableHash("items");

                if (!isItemsKey) return;

                lock (Sync)
                {
                    CurrentZdo = DescribeZdo(__instance);
                }
            });
        }

        public static void IsSameTypePrefix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            if (!InWorldConversion) return;

            SafeDiagnostic(() =>
            {
                object other = (__args != null && __args.Length > 0) ? __args[0] : null;
                var sb = new StringBuilder();
                sb.AppendLine("THIS ItemData:");
                sb.AppendLine(DescribeItemData(__instance));
                sb.AppendLine("OTHER ItemData:");
                sb.AppendLine(DescribeItemData(other));

                lock (Sync)
                {
                    LastComparison = sb.ToString().TrimEnd();
                }
            });
        }

        private static void WriteFailureReport(Exception ex, object inventoryInstance)
        {
            string currentInventory;
            string sourceZdo;
            string lastNamedItem;
            string lastTempItem;
            string lastComparison;
            string[] recent;

            lock (Sync)
            {
                currentInventory = CurrentInventory;
                sourceZdo = InventorySourceZdo;
                lastNamedItem = LastNamedItem;
                lastTempItem = LastTempItem;
                lastComparison = LastComparison;
                recent = RecentNamedItems.ToArray();
            }

            Write("---------------- CONVERSION FAILURE REPORT ----------------");
            Write("Inventory snapshot: " + (inventoryInstance != null ? DescribeInventory(inventoryInstance) : currentInventory));
            Write("Source ZDO candidate: " + sourceZdo);
            Write("Last Inventory.AddItem(string, ...) call:\n" + lastNamedItem);
            Write("Last Inventory.AddTempItem(int prefabHash, ...) call:\n" + lastTempItem);
            Write("Last ItemData.IsSameType comparison:\n" + lastComparison);

            if (recent.Length > 0)
            {
                Write("Recent named items in this inventory, oldest -> newest:");
                for (int i = 0; i < recent.Length; i++)
                    Write($"  [{i + 1:00}] {recent[i]}");
            }

            Write("Exception:\n" + ex);
            Write("-----------------------------------------------------------");
        }

        private static string DescribeZdo(object zdo)
        {
            if (zdo == null) return "<null ZDO>";

            try
            {
                var parts = new List<string>();
                parts.Add("type=" + zdo.GetType().FullName);

                foreach (string member in new[] { "m_uid", "m_position", "m_sector", "m_prefab", "m_type", "m_owner", "m_dataRevision" })
                {
                    object value = GetMemberValue(zdo, member);
                    if (value != null) parts.Add(member + "=" + FormatSimple(value));
                }

                foreach (string methodName in new[] { "GetPosition", "GetPrefab" })
                {
                    try
                    {
                        MethodInfo m = zdo.GetType().GetMethods(AllMethods)
                            .FirstOrDefault(x => x.Name == methodName && x.GetParameters().Length == 0);
                        if (m != null)
                        {
                            object value = m.Invoke(zdo, null);
                            if (value != null) parts.Add(methodName + "=" + FormatSimple(value));
                        }
                    }
                    catch { }
                }

                return string.Join(", ", parts);
            }
            catch (Exception ex)
            {
                return "<failed to describe ZDO: " + ex.GetType().Name + ": " + ex.Message + ">";
            }
        }

        private static int StableHash(string str)
        {
            unchecked
            {
                int hash1 = 5381;
                int hash2 = hash1;
                for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ str[i];
                    if (i == str.Length - 1 || str[i + 1] == '\0') break;
                    hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
                }
                return hash1 + (hash2 * 1566083941);
            }
        }

        private static string DescribeInventory(object inventory)
        {
            if (inventory == null) return "<null Inventory>";

            try
            {
                object name = GetMemberValue(inventory, "m_name");
                object width = GetMemberValue(inventory, "m_width");
                object height = GetMemberValue(inventory, "m_height");
                object items = GetMemberValue(inventory, "m_inventory");
                int? count = GetCollectionCount(items);

                return $"type={inventory.GetType().FullName}, name={FormatSimple(name)}, size={FormatSimple(width)}x{FormatSimple(height)}, loadedItemCount={(count.HasValue ? count.Value.ToString() : "?")}";
            }
            catch (Exception ex)
            {
                return "<failed to describe Inventory: " + ex.Message + ">";
            }
        }

        private static string DescribeItemData(object item)
        {
            if (item == null) return "  <null>";

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("  runtimeType=" + item.GetType().FullName);

                AppendMember(sb, item, "m_dropPrefab", describeUnityName: true);
                AppendMember(sb, item, "m_stack");
                AppendMember(sb, item, "m_durability");
                AppendMember(sb, item, "m_quality");
                AppendMember(sb, item, "m_variant");
                AppendMember(sb, item, "m_crafterID");
                AppendMember(sb, item, "m_crafterName");
                AppendMember(sb, item, "m_worldLevel");
                AppendMember(sb, item, "m_equipped");
                AppendMember(sb, item, "m_gridPos");

                object shared = GetMemberValue(item, "m_shared");
                if (shared == null)
                {
                    sb.AppendLine("  m_shared=<null>  <<< IMPORTANT");
                }
                else
                {
                    sb.AppendLine("  m_shared.runtimeType=" + shared.GetType().FullName);
                    AppendMember(sb, shared, "m_name", prefix: "  m_shared.");
                    AppendMember(sb, shared, "m_itemType", prefix: "  m_shared.");
                    AppendMember(sb, shared, "m_maxStackSize", prefix: "  m_shared.");
                    AppendMember(sb, shared, "m_maxQuality", prefix: "  m_shared.");
                    AppendMember(sb, shared, "m_weight", prefix: "  m_shared.");
                    AppendMember(sb, shared, "m_dlc", prefix: "  m_shared.");
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "  <failed to describe ItemData: " + ex + ">";
            }
        }

        private static void AppendMember(StringBuilder sb, object obj, string memberName, bool describeUnityName = false, string prefix = "  ")
        {
            object value = GetMemberValue(obj, memberName);
            string display = describeUnityName ? DescribeUnityObject(value) : FormatSimple(value);
            sb.AppendLine(prefix + memberName + "=" + display);
        }

        private static string DescribeUnityObject(object value)
        {
            if (value == null) return "<null>  <<< IMPORTANT";

            try
            {
                object name = GetMemberValue(value, "name");
                if (name != null)
                    return value.GetType().FullName + " name=" + FormatSimple(name);
            }
            catch
            {
                // Fall through to type-only output.
            }

            return "<" + value.GetType().FullName + ">";
        }

        private static string FormatArguments(MethodBase method, object[] args)
        {
            try
            {
                ParameterInfo[] parameters = method.GetParameters();
                var parts = new List<string>();
                int count = Math.Max(parameters.Length, args?.Length ?? 0);

                for (int i = 0; i < count; i++)
                {
                    string name = i < parameters.Length ? parameters[i].Name : "arg" + i;
                    object value = args != null && i < args.Length ? args[i] : null;
                    parts.Add(name + "=" + FormatSimple(value));
                }

                return method.DeclaringType?.FullName + "." + method.Name + "(" + string.Join(", ", parts.ToArray()) + ")";
            }
            catch (Exception ex)
            {
                return "<failed to format method arguments: " + ex.Message + ">";
            }
        }

        private static string FormatSimple(object value)
        {
            if (value == null) return "<null>";

            Type t = value.GetType();
            if (value is string s) return "\"" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
            if (t.IsPrimitive || t.IsEnum || value is decimal) return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

            if (value is IDictionary dictionary)
                return $"<{t.FullName} Count={dictionary.Count}>";
            if (value is ICollection collection)
                return $"<{t.FullName} Count={collection.Count}>";

            // Vector-like structs are useful, and their ToString() is generally safe.
            if (t.IsValueType)
            {
                try { return value.ToString(); }
                catch { return "<" + t.FullName + ">"; }
            }

            return "<" + t.FullName + ">";
        }

        private static object GetMemberValue(object instance, string name)
        {
            if (instance == null) return null;

            Type type = instance.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field.GetValue(instance);

                PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(instance, null);

                type = type.BaseType;
            }

            return null;
        }

        private static int? GetCollectionCount(object value)
        {
            if (value == null) return null;
            if (value is ICollection collection) return collection.Count;

            PropertyInfo count = value.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (count != null)
            {
                object result = count.GetValue(value, null);
                if (result is int i) return i;
            }

            return null;
        }

        private static Type FindType(string fullName)
        {
            Type viaHarmony = AccessTools.TypeByName(fullName);
            if (viaHarmony != null) return viaHarmony;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }

            return null;
        }

        private static string GetDiagnosticPath()
        {
            try
            {
                const string dockerConfig = "/config/bepinex";
                if (Directory.Exists(dockerConfig))
                    return Path.Combine(dockerConfig, "ValheimWorldConvertDiag.log");
            }
            catch
            {
                // Fall back below.
            }

            try
            {
                return Path.Combine(Paths.ConfigPath, "ValheimWorldConvertDiag.log");
            }
            catch
            {
                return Path.Combine(Environment.CurrentDirectory, "ValheimWorldConvertDiag.log");
            }
        }

        private static void SafeDiagnostic(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                try { Write("Diagnostic code caught its own exception and ignored it: " + ex); }
                catch { }
            }
        }

        private static void Write(string message)
        {
            string line = "[ValheimWorldConvertDiag] " + message;

            try
            {
                Log?.LogWarning(message);
            }
            catch
            {
                // Keep going so file logging still works.
            }

            try
            {
                lock (Sync)
                {
                    File.AppendAllText(
                        DiagnosticFile,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + line + Environment.NewLine);
                }
            }
            catch
            {
                // Never let diagnostics alter Valheim's behavior.
            }
        }

        private const BindingFlags AllMethods =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    }
}
