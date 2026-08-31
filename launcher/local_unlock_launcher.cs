using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

internal static class LocalUnlockLauncher
{
    private const uint PageExecuteReadWrite = 0x40;
    private static readonly object LogGate = new object();
    private static readonly HashSet<string> Patched = new HashSet<string>(StringComparer.Ordinal);
    private static readonly Target[] Targets =
    {
        new Target("account.CN3OHofjPa", "hy3KihF9xvt2y8HqZYt.XlSQv8Fs6mqx3mINYkI", "EyIOL3b7uw", new byte[] { 0x31, 0xc0, 0xc6, 0x41, 0x78, 0x00, 0xc3 }),
        new Target("account.RgiTYlXbQF", "hy3KihF9xvt2y8HqZYt.XlSQv8Fs6mqx3mINYkI", "RgiTYlXbQF", new byte[] { 0xb0, 0x01, 0xc3 }),
        new Target("decoder.IsUniversalDecoder", "TianChaoXiaoJiangDecoder.TianChaoXiaoJiangCodecPlugin", "get_IsUniversalDecoder", new byte[] { 0xb0, 0x01, 0xc3 }),
        new Target("mcp.IsMethodAllowed", "Fatbeans.Vip.Mcp.McpCore", "IsMethodAllowed", new byte[] { 0xb0, 0x01, 0xc3 }),
        new Target("mcp.IsFreeMethod", "Fatbeans.Vip.Mcp.McpCore", "IsFreeMethod", new byte[] { 0xb0, 0x01, 0xc3 }),
        new Target("api.AuthorizeStart", "Fatbeans.Vip.Api.ApiCore", "AuthorizeStart", new byte[] { 0xb0, 0x01, 0xc3 }),
        new Target("api.AuthorizeRequest", "Fatbeans.Vip.Api.ApiCore", "AuthorizeRequest", new byte[] { 0xb0, 0x01, 0xc3 }),
        new Target("webhook.Authorize", "Fatbeans.Vip.WebHook.WebHookCore", "Authorize", new byte[] { 0xb0, 0x01, 0xc3 }),
        new Target("http.AuthorizeCustomRule", "Fatbeans.Vip.HttpBreakPoint.HttpBreakPointVipCore", "AuthorizeCustomRule", new byte[] { 0xb0, 0x01, 0xc3 }),
        new Target("http.AuthorizeBatchOps", "Fatbeans.Vip.HttpBreakPoint.HttpBreakPointVipCore", "AuthorizeBatchOps", new byte[] { 0xb0, 0x01, 0xc3 }),
        new Target("decode.AuthorizeFullOutput", "VRD0L3fy2ViMwgPbecPj.Tekx29fyuXDI0mYmbjjn", "AuthorizeFullOutput", new byte[] { 0xb0, 0x01, 0xc3 }),
        new Target("decode.FormatForDisplay", "VRD0L3fy2ViMwgPbecPj.Tekx29fyuXDI0mYmbjjn", "FormatForDisplay", typeof(string), new byte[] { 0x48, 0x8b, 0xc2, 0xc3 }),
        new Target("decode.DisplayVipFallback", "Fatbeans.Controls.DecodePluginControl", "pa3lq92ZvDJ", typeof(string), new byte[] { 0x48, 0x8b, 0xc1, 0xc3 }),
        new Target("mcp.GateAllowed", null, "GateAllowed", new byte[] { 0xb0, 0x01, 0xc3 })
    };

    private static string LogPath;
    private static int PatchBusy;

    [STAThread]
    private static int Main(string[] args)
    {
        string target = ResolveTarget(args);
        if (!File.Exists(target))
        {
            Console.Error.WriteLine("target not found: " + target);
            return 2;
        }

        LogPath = Environment.GetEnvironmentVariable("FATBEANS_LOCAL_UNLOCK_LOG");
        if (string.IsNullOrEmpty(LogPath))
            LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fatbeans.local-unlock.log");
        string configPath = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
        if (!File.Exists(configPath))
            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FatbeansCreater.exe.config");
        TrySetConfig(configPath);
        Log("START HOST=" + GetHostPath() + " TARGET=" + target + " CONFIG=" + configPath);

        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        using (var timer = new Timer(_ => PatchLoadedAssemblies(), null, 0, 150))
        {
            Assembly targetAssembly;
            try
            {
                targetAssembly = Assembly.LoadFrom(target);
                Log("MAIN " + targetAssembly.FullName + " ENTRY=" + targetAssembly.EntryPoint);
                PatchLoadedAssemblies();
            }
            catch (Exception ex)
            {
                Log("LOAD_ERROR " + ex);
                return 3;
            }

            MethodInfo entryPoint = targetAssembly.EntryPoint;
            if (entryPoint == null)
            {
                Log("ENTRY_MISSING");
                return 4;
            }

            object[] entryArguments = entryPoint.GetParameters().Length == 0
                ? null
                : new object[] { args.Skip(1).ToArray() };
            try
            {
                entryPoint.Invoke(null, entryArguments);
                return 0;
            }
            catch (TargetInvocationException ex)
            {
                Log("ENTRY_ERROR " + (ex.InnerException ?? ex));
                return 5;
            }
            catch (Exception ex)
            {
                Log("ENTRY_ERROR " + ex);
                return 6;
            }
        }
    }

    private static string ResolveTarget(string[] args)
    {
        if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
            return Path.GetFullPath(args[0]);

        string overrideTarget = Environment.GetEnvironmentVariable("FATBEANS_LOCAL_UNLOCK_TARGET");
        if (!string.IsNullOrWhiteSpace(overrideTarget))
            return Path.GetFullPath(overrideTarget);

        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string coreTarget = Path.Combine(baseDirectory, "FatbeansCreater.core.exe");
        if (File.Exists(coreTarget))
            return coreTarget;

        // Keep the launcher usable with an untouched installation as well.
        return Path.Combine(baseDirectory, "FatbeansCreater.exe");
    }

    private static string GetHostPath()
    {
        try
        {
            Process process = Process.GetCurrentProcess();
            return process.MainModule == null ? "" : process.MainModule.FileName;
        }
        catch
        {
            return "";
        }
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
    {
        Log("LOAD " + args.LoadedAssembly.FullName);
        PatchLoadedAssemblies();
    }

    private static void PatchLoadedAssemblies()
    {
        if (Interlocked.Exchange(ref PatchBusy, 1) != 0) return;
        try
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Target target in Targets)
            {
                foreach (Assembly assembly in assemblies)
                {
                    foreach (Type type in GetTypes(assembly))
                    {
                        if (target.TypeName != null && !string.Equals(type.FullName, target.TypeName, StringComparison.Ordinal))
                            continue;
                        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        {
                            if (!string.Equals(method.Name, target.MethodName, StringComparison.Ordinal) ||
                                (target.ReturnType != null && method.ReturnType != target.ReturnType))
                                continue;
                            TryPatch(target, type, method);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log("SCAN_ERROR " + ex.GetType().Name + " " + ex.Message);
        }
        finally
        {
            Volatile.Write(ref PatchBusy, 0);
        }
    }

    private static void TryPatch(Target target, Type type, MethodInfo method)
    {
        string methodKey = target.Label + "|" + type.Assembly.FullName + "|" + type.FullName + "|" + method.MetadataToken.ToString("x8");
        if (Patched.Contains(methodKey)) return;

        try
        {
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            IntPtr managedEntry = method.MethodHandle.GetFunctionPointer();
            IntPtr nativeEntry = ResolveEntry(managedEntry);
            if (nativeEntry == IntPtr.Zero) return;

            byte[] before = new byte[target.Bytes.Length];
            Marshal.Copy(nativeEntry, before, 0, before.Length);
            uint oldProtection;
            if (!VirtualProtect(nativeEntry, (UIntPtr)target.Bytes.Length, PageExecuteReadWrite, out oldProtection))
                throw new InvalidOperationException("VirtualProtect failed: " + Marshal.GetLastWin32Error());
            Marshal.Copy(target.Bytes, 0, nativeEntry, target.Bytes.Length);
            FlushInstructionCache(GetCurrentProcess(), nativeEntry, (UIntPtr)target.Bytes.Length);
            uint ignored;
            VirtualProtect(nativeEntry, (UIntPtr)target.Bytes.Length, oldProtection, out ignored);

            Patched.Add(methodKey);
            Log("PATCH " + target.Label + " TYPE=" + type.FullName + " METHOD=" + method.Name +
                " ENTRY=" + nativeEntry + " BEFORE=" + Hex(before) + " AFTER=" + Hex(target.Bytes));
        }
        catch (Exception ex)
        {
            Log("PATCH_ERROR " + target.Label + " TYPE=" + type.FullName + " METHOD=" + method.Name + " " + ex.GetType().Name + " " + ex.Message);
        }
    }

    private static IntPtr ResolveEntry(IntPtr address)
    {
        IntPtr current = address;
        for (int depth = 0; depth < 12; depth++)
        {
            for (int offset = 0; offset <= 32; offset++)
            {
                IntPtr candidate = Add(current, offset);
                if (ReadByte(candidate) == 0x48 && ReadByte(Add(candidate, 1)) == 0xb8 &&
                    ReadByte(Add(candidate, 10)) == 0xff && ReadByte(Add(candidate, 11)) == 0xe0)
                    return Marshal.ReadIntPtr(Add(candidate, 2));
            }

            byte opcode = ReadByte(current);
            if (opcode == 0xe9)
            {
                current = Add(Add(current, 5), Marshal.ReadInt32(Add(current, 1)));
                continue;
            }
            if (opcode == 0xeb)
            {
                current = Add(Add(current, 2), (sbyte)ReadByte(Add(current, 1)));
                continue;
            }
            if (opcode == 0xff && ReadByte(Add(current, 1)) == 0x25)
            {
                current = Marshal.ReadIntPtr(Add(Add(current, 6), Marshal.ReadInt32(Add(current, 2))));
                continue;
            }
            return current;
        }
        return current;
    }

    private static byte ReadByte(IntPtr address)
    {
        return Marshal.ReadByte(address);
    }

    private static IntPtr Add(IntPtr address, int offset)
    {
        return new IntPtr(address.ToInt64() + offset);
    }

    private static Type[] GetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(type => type != null).ToArray(); }
        catch { return new Type[0]; }
    }

    private static void TrySetConfig(string configPath)
    {
        if (File.Exists(configPath))
            AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE", configPath);
    }

    private static string Hex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", string.Empty);
    }

    private static void Log(string message)
    {
        try
        {
            lock (LogGate)
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
            }
        }
        catch { }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtection, out uint oldProtection);

    [DllImport("kernel32.dll")]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private sealed class Target
    {
        public Target(string label, string typeName, string methodName, byte[] bytes)
            : this(label, typeName, methodName, typeof(bool), bytes)
        {
        }

        public Target(string label, string typeName, string methodName, Type returnType, byte[] bytes)
        {
            Label = label;
            TypeName = typeName;
            MethodName = methodName;
            ReturnType = returnType;
            Bytes = bytes;
        }

        public readonly string Label;
        public readonly string TypeName;
        public readonly string MethodName;
        public readonly Type ReturnType;
        public readonly byte[] Bytes;
    }
}
