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

    protected override object NewDriveDynamicParameters()
    {
        var paramDict = new RuntimeDefinedParameterDictionary();

        var param = new RuntimeDefinedParameter(
            "PowerShellAppDomain", // Parametername
            typeof(AppDomain), // Typ des Parameters
            [new ParameterAttribute() { Mandatory = true }]
        );

        paramDict.Add("PowerShellAppDomain", param);
        return paramDict;
    }
    protected override PSDriveInfo NewDrive(PSDriveInfo drive)
    {
        if (DynamicParameters is RuntimeDefinedParameterDictionary dp)
        {
            dp.TryGetValue("PowerShellAppDomain", out var appdomain);
            if (appdomain?.Value is AppDomain appDomain)
                this.AppDomain = appDomain;
            else
                this.AppDomain = AppDomain.CurrentDomain;
        }
        else
            this.AppDomain = AppDomain.CurrentDomain;

        generateNamespaces();

        this.AppDomain.AssemblyLoad += onLoadAssembly;

        return base.NewDrive(drive);
    }

    public AppDomain? AppDomain;
    static internal readonly OrderedDictionary<string, List<Assembly>> NamespacesInAssembly = [];

    private void generateNamespaces(IEnumerable<Assembly>? assemblies = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        assemblies ??= this.AppDomain!.GetAssemblies();
        foreach (var ass in assemblies)
        {
            var asns = new List<string>();
            foreach (var typ in ass.GetExportedTypes())
                if (!asns.Contains(typ.Namespace ?? ""))
                    asns.Add(typ.Namespace ?? "");

            foreach (var ns in asns)
                if (NamespacesInAssembly.TryGetValue(ns, out var ns2))
                    ns2.Add(ass);
                else
                    NamespacesInAssembly.Add(ns, [ass]);
        }
        WriteWarning($"generateNamespaces took {sw.ElapsedMilliseconds}");
    }

    private void onLoadAssembly(object? sender, AssemblyLoadEventArgs args)
    {
        WriteVerbose("LoadAssembly:" + args.LoadedAssembly.GetName().Name);
        generateNamespaces([args.LoadedAssembly]);
    }


    protected override bool IsItemContainer(string path)
    {
        return NamespacesInAssembly.ContainsKey(toNativePath(path));
    }

    private string toNativePath(string path)
    {
        return path[2..].Replace('\\', '.');
    }

    protected override void GetChildItems(string path, bool recurse)
    {
        this.GetChildItems(path, recurse, 1);
    }

    protected override void GetChildItems(string path, bool recurse, uint depth)
    {
        if (path == ".\\" || path == "")
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
                    GetChildItems(".\\" + ns, recurse, depth - 1);
            }
            foreach (var item in GetTypes(path))
                WriteItemObject(item, item.FullName, false);
        }
        else
        {
            foreach (var ns in NamespacesInAssembly.Keys.Where(ns => ns.StartsWith(toNativePath(path) + ".") && (ns.Count(s => s == '.') == (toNativePath(path) + ".").Count(s => s == '.'))).Order())
            {
                WriteItemObject(new NamespaceType(ns), ns.Replace('.', '\\'), true);
                if (recurse)
                    GetChildItems(".\\" + ns, recurse, --depth);
            }
            foreach (var item in GetTypes(path))
                WriteItemObject(item, item.FullName, false);
        }
    }

    protected override void GetChildNames(string path, ReturnContainers returnContainers)
    {
        if (path == ".\\" || path.Length == 0)
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
            foreach (var ns in NamespacesInAssembly.Keys.Where(ns => ns.StartsWith(toNativePath(path))))
                WriteItemObject(new NamespaceType(ns.Replace(toNativePath(path) + '.', "")), ns.Replace(toNativePath(path) + '.', ""), true);
            foreach (var item in GetTypes(path))
                WriteItemObject(item, item.Name, false);
        }
    }


    protected override string GetChildName(string path)
    {
        return path; //.Substring(path.LastIndexOf('.'));
    }

    IEnumerable<Type> GetTypes(string path)
    {
        string ns = toNativePath(path);
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
        if (NamespacesInAssembly.ContainsKey(toNativePath(path)))
            return true; // Simpler Test: Alle Pfade existieren
        if (Type.GetType(toNativePath(path), false) != null)
            return true;
        return false;
    }

    protected override void GetItem(string path)
    {
        if (IsItemContainer(path))
            WriteItemObject(new NamespaceType(path), path, true);
        else
            WriteItemObject(Type.GetType(toNativePath(path)), path, false);
    }

    protected override bool HasChildItems(string path)
    {
        return NamespacesInAssembly.ContainsKey(toNativePath(path));
    }

    protected override bool IsValidPath(string path)
    {
        return true;
    }

}
