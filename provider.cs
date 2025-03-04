using System.Linq;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Reflection;
using System;

[CmdletProvider("TypeProvider", ProviderCapabilities.None)]
public class TypeProvider : NavigationCmdletProvider
{
    public override char ItemSeparator => '.';
    public override char AltItemSeparator => '.';
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

        this.AppDomain.AssemblyLoad += onLoadAssembly;

        generateNamespaces();

        return base.NewDrive(drive);
    }

    private void generateNamespaces(IEnumerable<Assembly>? assemblies = null)
    {
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
    }

    private void onLoadAssembly(object? sender, AssemblyLoadEventArgs args)
    {
        generateNamespaces([args.LoadedAssembly]);
    }

    AppDomain? AppDomain;

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
        if (path == ".\\")
        {
            var rootNs = NamespacesInAssembly.Keys.DistinctBy(a => a.Substring(0, a.IndexOf('.') == -1 ? a.Length : a.IndexOf('.'))).Order();
            foreach (var ns in rootNs)
                WriteItemObject(new Namespace(ns), path + "\\" + ns.Replace('.', '\\'), true);
        }
        else
        {
            foreach (var ns in NamespacesInAssembly.Keys.Where(ns => ns.StartsWith(toNativePath(path))))
                WriteItemObject(new Namespace(ns.Replace(toNativePath(path) + '.', "")), path, true);
            foreach (var item in GetTypes(path))
                WriteItemObject(item, path, false);
        }
    }

    protected override void GetChildNames(string path, ReturnContainers returnContainers)
    {
        this.GetChildItems(path, false, 1);
    }


    protected override string GetChildName(string path) {
        return path.Substring(path.LastIndexOf('.'));
    } 

    IEnumerable<Type> GetTypes(string path)
    {
        string ns = toNativePath(path);
        if (NamespacesInAssembly.TryGetValue(ns, out var relAss))
            // var relAss = NamespacesInAssembly[ns];
            return relAss.SelectMany(a => a.GetExportedTypes().Where(t => t.Namespace == ns)).OrderBy(t => t.Name);
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
            WriteItemObject($"Item: {path}", path, true);
        else
            WriteItemObject($"Item: {Type.GetType(toNativePath(path))}", path, false);
    }

    protected override bool HasChildItems(string path)
    {
        return NamespacesInAssembly.ContainsKey(toNativePath(path));
    }

    protected override bool IsValidPath(string path)
    {
        return true;
    }

    private readonly static Dictionary<string, List<Assembly>> NamespacesInAssembly = [];
}
