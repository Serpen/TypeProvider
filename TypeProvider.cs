using System.Linq;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Reflection;
using System;

namespace Serpen.PS;

[CmdletProvider("TypeProvider", ProviderCapabilities.None)]
public class TypeProvider : NavigationCmdletProvider
{

    private const string ParamAppDomain = "PowerShellAppDomain";
    protected override object NewDriveDynamicParameters()
    {
        var paramDict = new RuntimeDefinedParameterDictionary();

        var param = new RuntimeDefinedParameter(
            ParamAppDomain, // Parametername
            typeof(AppDomain), // Typ des Parameters
            [new ParameterAttribute() { Mandatory = true }]
        );

        paramDict.Add(ParamAppDomain, param);
        return paramDict;
    }
    protected override PSDriveInfo NewDrive(PSDriveInfo drive)
    {
        if (DynamicParameters is RuntimeDefinedParameterDictionary dp)
        {
            dp.TryGetValue(ParamAppDomain, out var appdomain);
            if (appdomain?.Value is AppDomain appDomain)
                AppDomain = appDomain;
            else
                AppDomain = AppDomain.CurrentDomain;
        }
        else
            AppDomain = AppDomain.CurrentDomain;

        generateNamespaces();

        AppDomain.AssemblyLoad += onLoadAssembly;

        return base.NewDrive(drive);
    }

    #region Native

    static AppDomain? AppDomain;
    static internal readonly OrderedDictionary<string, List<Assembly>> NamespacesInAssembly = [];

    private void generateNamespaces(IEnumerable<Assembly>? assemblies = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        assemblies ??= AppDomain!.GetAssemblies();
        foreach (var ass in assemblies)
        {
            var asns = new List<string>();
            foreach (var typ in ass.GetExportedTypes())
                if (!asns.Contains(typ.Namespace ?? ""))
                    foreach (var nsPart in getNsParts(typ.Namespace ?? ""))
                        asns.Add(nsPart);

            foreach (var ns in asns)
                if (NamespacesInAssembly.TryGetValue(ns, out var ns2))
                {
                    if (!ns2.Contains(ass))
                        ns2.Add(ass);
                }
                else
                    NamespacesInAssembly.Add(ns, [ass]);
        }
        WriteWarning($"generateNamespaces took {sw.ElapsedMilliseconds}");
    }

    IEnumerable<string> getNsParts(string ns)
    {
        for (int i = 1; i < ns.Length - 1; i++)
            if (ns[i] == '.')
                yield return ns.Substring(0, i);
        yield return ns;
    }

    private void onLoadAssembly(object? sender, AssemblyLoadEventArgs args)
    {
        WriteVerbose("LoadAssembly:" + args.LoadedAssembly.GetName().Name);
        generateNamespaces([args.LoadedAssembly]);
    }

    private string toNamespacePath(string path)
    {
        return path.Replace('\\', '.').TrimEnd('.');
    }

    #endregion


    protected override bool IsItemContainer(string path)
    {
        return NamespacesInAssembly.ContainsKey(toNamespacePath(path));
    }

    protected override void GetChildItems(string path, bool recurse)
    {
        if (path == "")
        {
            var rootNs = NamespacesInAssembly.Keys
                .Where(n => !String.IsNullOrEmpty(n))
                .Select(n => n.Substring(0, (n.IndexOf('.') == -1) ? n.Length : n.IndexOf('.')))
                .Distinct()
                .Order();
            foreach (var ns in rootNs)
            {
                WriteItemObject(new NamespaceType(ns), ns, true);
                if (recurse)
                    GetChildItems(ns, recurse);
            }
            foreach (var item in GetTypes(path))
                WriteItemObject(item, item.FullName, false);
        }
        else
        {
            foreach (var ns in NamespacesInAssembly.Keys.Where(ns => ns.StartsWith(toNamespacePath(path) + ".") && (ns.Count(s => s == '.') == (toNamespacePath(path) + ".").Count(s => s == '.'))).Order())
            {
                WriteItemObject(new NamespaceType(ns), ns.Replace('.', '\\'), true);
                if (recurse)
                    GetChildItems(ns, recurse);
            }
            foreach (var item in GetTypes(path))
                WriteItemObject(item, item.FullName, false);
        }
    }

    protected override void GetChildNames(string path, ReturnContainers returnContainers)
    {
        if (path == "")
        {
            var rootNs = NamespacesInAssembly.Keys
                .Where(n => !String.IsNullOrEmpty(n))
                .Select(n => n.Substring(0, (n.IndexOf('.') == -1) ? n.Length : n.IndexOf('.')))
                .Distinct()
                .Order();
            foreach (var ns in rootNs)
                WriteItemObject(new NamespaceType(ns), ns, true);
            foreach (var item in GetTypes(path))
                WriteItemObject(item, item.Name, false);
        }
        else
        {
            foreach (var ns in NamespacesInAssembly.Keys.Where(ns => ns.StartsWith(toNamespacePath(path))))
                WriteItemObject(new NamespaceType(ns.Replace(toNamespacePath(path) + '.', "")), ns.Replace(toNamespacePath(path) + '.', ""), true);
            foreach (var item in GetTypes(path))
                WriteItemObject(item.Name, item.Name, false);
        }
    }

    protected override string GetChildName(string path)
    {
        return base.GetChildName(path);
    }

    IEnumerable<Type> GetTypes(string path)
    {
        string ns = toNamespacePath(path);
        if (NamespacesInAssembly.TryGetValue(ns, out var relAss))
            return relAss
                .SelectMany(a => a.GetExportedTypes()
                .Where(t => t.Namespace == ns || ns == "" && t.Namespace == null))
                .OrderBy(t => t.Name);
        else
            return System.Array.Empty<Type>();
    }

    protected override bool ItemExists(string path)
    {
        if (NamespacesInAssembly.ContainsKey(toNamespacePath(path)))
            return true;
        if (Type.GetType(toNamespacePath(path), false) != null)
            return true;
        return false;
    }

    protected override void GetItem(string path)
    {
        if (IsItemContainer(path))
            WriteItemObject(new NamespaceType(path), path, true);
        else
            WriteItemObject(Type.GetType(toNamespacePath(path)), path, false);
    }

    protected override bool HasChildItems(string path)
    {
        return NamespacesInAssembly.ContainsKey(toNamespacePath(path));
    }

    protected override bool IsValidPath(string path)
    {
        return true;
    }

}
