using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Reflection;
using System;
using System.Text;

namespace Serpen.PS;

[CmdletProvider("TypeProvider", ProviderCapabilities.None)]
public class TypeProvider : NavigationCmdletProvider, IPropertyCmdletProvider
{

    private const string ParamAppDomain = "PowerShellAppDomain";
    protected override object NewDriveDynamicParameters()
        => new RuntimeDefinedParameterDictionary() {
            {ParamAppDomain,
            new RuntimeDefinedParameter(ParamAppDomain, typeof(AppDomain), [new ParameterAttribute() { Mandatory = true }])}};

    protected override PSDriveInfo NewDrive(PSDriveInfo drive)
    {
        if (DynamicParameters is RuntimeDefinedParameterDictionary dicparam)
            if (dicparam.TryGetValue(ParamAppDomain, out var appdomain))
                if (appdomain?.Value is AppDomain appDomain)
                    AppDomain = appDomain;

        AppDomain ??= AppDomain.CurrentDomain;

        GenerateNamespaces();

        AppDomain.AssemblyLoad += onLoadAssembly;

        return base.NewDrive(drive);
    }

    #region Native

    static AppDomain? AppDomain;
    static internal readonly OrderedDictionary<string, List<Assembly>> NamespacesInAssembly = [];

    private void GenerateNamespaces(IEnumerable<Assembly>? assemblies = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        assemblies ??= AppDomain!.GetAssemblies();

        foreach (var ass in assemblies)
        {
            var asns = new List<string>();
            foreach (var typ in ass.GetExportedTypes())
            {
                if (Stopping) return;
                if (!asns.Contains(typ.Namespace ?? ""))
                    foreach (var nsPart in getNsParts(typ.Namespace ?? ""))
                        asns.Add(nsPart);
            }

            foreach (var ns in asns) {
                if (Stopping) return;
                if (NamespacesInAssembly.TryGetValue(ns, out var ns2))
                {
                    if (!ns2.Contains(ass))
                        ns2.Add(ass);
                }
                else
                    NamespacesInAssembly.Add(ns, [ass]);
            }
        }
        sw.Stop();
        // WriteWarning($"generateNamespaces took {sw.ElapsedMilliseconds}");
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
        GenerateNamespaces([args.LoadedAssembly]);
    }

    private string toNamespacePath(string path)
    {
        return path.Replace(base.ItemSeparator, '.').TrimEnd('.');
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
                if (Stopping) return;
                WriteItemObject(new NamespaceType(ns), ns.Replace('.', base.ItemSeparator), true);
                if (recurse)
                    GetChildItems(ns, recurse);
            }
            foreach (var item in GetTypes(path))
            {
                if (Stopping) return;
                WriteItemObject(item, item.FullName.Replace('.', base.ItemSeparator), false);
            }
        }
        else
        {
            foreach (var ns in NamespacesInAssembly.Keys.Where(ns => ns.StartsWith(toNamespacePath(path) + ".") && (ns.Count(s => s == '.') == (toNamespacePath(path) + ".").Count(s => s == '.'))).Order())
            {
                if (Stopping) return;
                WriteItemObject(new NamespaceType(ns), ns.Replace('.', base.ItemSeparator), true);
                if (recurse)
                    GetChildItems(ns, recurse);
            }
            foreach (var item in GetTypes(path))
            {
                if (Stopping) return;
                WriteItemObject(item, item.FullName.Replace('.', base.ItemSeparator), false);
            }
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
            {
                if (Stopping) return;
                WriteItemObject(new NamespaceType(ns).Name, ns, true);
            }
            foreach (var item in GetTypes(path))
            {
                if (Stopping) return;
                WriteItemObject(item.Name, item.Name, false);
            }
        }
        else
        {
            foreach (var ns in NamespacesInAssembly.Keys.Where(ns => ns.StartsWith(toNamespacePath(path))))
            {
                if (Stopping) return;
                WriteItemObject(new NamespaceType(ns.Replace(toNamespacePath(path) + '.', "")).Name, ns.Replace(toNamespacePath(path) + '.', ""), true);
            }
            foreach (var item in GetTypes(path))
            {
                if (Stopping) return;
                WriteItemObject(item.Name, item.Name, false);
            }
        }
    }

    protected override string GetChildName(string path)
    {
        if (path != "")
            return base.GetChildName(path);
        return "";
    }

    IEnumerable<Type> GetTypes(string path)
    {
        string ns = toNamespacePath(path);
        if (NamespacesInAssembly.TryGetValue(ns, out var relAss))
            return new List<Assembly>(relAss)
                .SelectMany(a => this.Force ? a.GetTypes() : a.GetExportedTypes())
                // .ToArray()
                .Where(t => t.Namespace == ns || ns == "" && t.Namespace == null)
                .OrderBy(t => t.Name);
        else
            return System.Array.Empty<Type>();
    }

    protected override bool ItemExists(string path)
    {
        if (NamespacesInAssembly.ContainsKey(toNamespacePath(path)))
            return true;

        if (FindType(path) != null)
            return true;
        return false;
    }

    // Only Types within same assembly can be found by nameonly
    // Searching the Namespaces Assembly List for that name
    Type FindType(string pspath)
    {
        Type? retType = Type.GetType(toNamespacePath(pspath), false, true);

        if (retType == null)
        {
            var parentNs = toNamespacePath(GetParentPath(pspath, ""));
            if (NamespacesInAssembly.TryGetValue(parentNs, out var asslist))
            {
                retType = asslist
                    .SelectMany(a => a.GetExportedTypes())
                    .FirstOrDefault(t => t.FullName == toNamespacePath(pspath));
            }
        }
        return retType;
    }

    protected override void GetItem(string path)
    {
        if (IsItemContainer(path))
            WriteItemObject(new NamespaceType(path), path, true);
        else
            WriteItemObject(FindType(path), path, false);
    }

    protected override bool HasChildItems(string path)
    {
        return NamespacesInAssembly.ContainsKey(toNamespacePath(path));
    }

    protected override bool IsValidPath(string path)
    {
        return true;
    }

    #region Invoke Constructor

    protected override void InvokeDefaultAction(string path)
    {
        object[] argArray = [];
        var typ = FindType(path);
        if (DynamicParameters is RuntimeDefinedParameterDictionary dicparam)
            if (dicparam.TryGetValue("Arguments", out var args))
                argArray = args?.Value as object[];

        var instance = System.Activator.CreateInstance(typ, argArray);
        this.WriteItemObject(instance, path, false);
    }

    protected override object InvokeDefaultActionDynamicParameters(string path)
    => new RuntimeDefinedParameterDictionary() {
            {"Arguments",
            new RuntimeDefinedParameter("Arguments", typeof(object[]), [new ParameterAttribute() {Position=0, HelpMessage = "Constructor Arguments"} ])}};

    #endregion

    #region Property

    public void GetProperty(string path, Collection<string>? providerSpecificPickList)
    {
        if (!IsItemContainer(path))
        {
            var t = FindType(path);

            var propertyResults = new PSObject();
            var dicMemDef = new OrderedDictionary<string, List<string>>();
            foreach (var mem in
                t!.GetMembers()
                    // .DistinctBy(m => m.Name)
                    .Where(m => providerSpecificPickList == null
                        || providerSpecificPickList.Count == 0
                        || providerSpecificPickList.Contains("*")
                        || providerSpecificPickList.Contains(m.Name))
                    .OrderBy(m => m.Name)
            )
            {
                if (Stopping) return;
                if (dicMemDef.TryGetValue(mem.Name, out var defs))
                    defs.Add(MemberDefinition(mem));
                else
                    dicMemDef.Add(mem.Name, [MemberDefinition(mem)]);
            }
            foreach (var mem in dicMemDef)
                propertyResults.Properties.Add(new PSNoteProperty(mem.Key, mem.Value));
            WritePropertyObject(propertyResults, path);
        }
    }

    private string MemberDefinition(MemberInfo mem)
    {
        var sb = new StringBuilder();
        if (mem is MethodInfo metInfo)
        {
            sb.Append(GetTypeAccelerated(metInfo.ReturnType));
            sb.Append(' ');
            sb.Append(mem.Name);
            sb.Append('(');
            sb.Append(String.Join(',', metInfo.GetParameters()
                .Select(p => $"{GetTypeAccelerated(p.ParameterType)} {p.Name}")));
            sb.Append(')');
        }
        else if (mem is PropertyInfo propInfo)
        {
            sb.Append(GetTypeAccelerated(propInfo.PropertyType));
            sb.Append(' ');
            sb.Append(mem.Name);
        }
        else if (mem is ConstructorInfo conInfo)
        {
            sb.Append("new(");
            sb.Append(String.Join(',', conInfo.GetParameters()
                .Select(p => $"{GetTypeAccelerated(p.ParameterType)} {p.Name}")));
            sb.Append(')');
        }
        else
        {
            sb.Append(mem.Name);
        }
        return sb.ToString();
    }

    static Lazy<Dictionary<Type, string>> TypeAccelerators = new(() =>
    {
        Type TypeAccelerators = typeof(PSObject).Assembly.GetType("System.Management.Automation.TypeAccelerators");
        var GetProp = TypeAccelerators.GetProperty("Get");
        var getgetMethod = GetProp.GetGetMethod();
        var dicUnknown = getgetMethod.Invoke(null, null);
        Dictionary<string, Type> dic = (Dictionary<string, Type>)dicUnknown; //  TypeAccelerators.InvokeMember("get_Get", BindingFlags.Static | BindingFlags.InvokeMethod, null, null, null);

        Dictionary<Type, string> ret = new();
        foreach (var typ in dic)
            try
            {
                ret.Add(typ.Value, typ.Key);
            }
            catch { }
        return ret;
    }

    );

    string GetTypeAccelerated(Type typ)
    {
        //[psobject].assembly.gettype("System.Management.Automation.TypeAccelerators")::Get
        if (TypeAccelerators.Value.TryGetValue(typ, out var retstring))
            return retstring;
        else
            return typ.ToString();
    }

    public object? GetPropertyDynamicParameters(string path, Collection<string>? providerSpecificPickList)
    {
        return null;
    }

    public void SetProperty(string path, PSObject propertyValue)
    {
        throw new NotImplementedException();
    }

    public object? SetPropertyDynamicParameters(string path, PSObject propertyValue)
    {
        return null;
    }

    public void ClearProperty(string path, Collection<string> propertyToClear)
    {
        throw new NotImplementedException();
    }

    public object? ClearPropertyDynamicParameters(string path, Collection<string> propertyToClear)
    {
        return null;
    }

    #endregion
}
