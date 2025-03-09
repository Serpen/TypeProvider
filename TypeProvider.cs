using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Reflection;
using System;
using System.Text;
using MethodBase = System.Reflection.MethodBase;

namespace Serpen.PS;

[CmdletProvider("TypeProvider", ProviderCapabilities.None)]

[OutputType([typeof(NamespaceType), typeof(Type)], ProviderCmdlet = ProviderCmdlet.GetChildItem)]
[OutputType([typeof(NamespaceType), typeof(Type)], ProviderCmdlet = ProviderCmdlet.GetItem)]
[OutputType([typeof(object), typeof(Type)], ProviderCmdlet = ProviderCmdlet.InvokeItem)]

[OutputType(typeof(NamespaceType), ProviderCmdlet = ProviderCmdlet.GetItem)]
[OutputType(typeof(Type), ProviderCmdlet = ProviderCmdlet.GetItem)]
public sealed class TypeProvider : NavigationCmdletProvider, IPropertyCmdletProvider
{
    protected override PSDriveInfo NewDrive(PSDriveInfo drive)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {drive}");
        if (drive == null)
            throw new ArgumentNullException(nameof(drive));

        if (drive.Root != "" && !IsItemContainer(drive.Root))
            throw new DriveNotFoundException("Unable to create a drive with the specified root. The root path does not exist.");

        return base.NewDrive(drive);
    }

    protected override ProviderInfo Start(ProviderInfo providerInfo)
    {
        GenerateNamespaces();

        AppDomain.AssemblyLoad += onLoadAssembly;

        return base.Start(providerInfo);
    }

    #region Native

    static readonly AppDomain AppDomain = AppDomain.CurrentDomain;
    static internal readonly Dictionary<string, List<Assembly>> NamespacesInAssemblies = [];
    static internal IOrderedEnumerable<string> rootNamespaces;

    static object LockgenNs = new object();

    private void GenerateNamespaces(IEnumerable<Assembly>? assemblies = null)
    {
        lock (LockgenNs) // didnt work as intended
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            assemblies ??= AppDomain.GetAssemblies();
            // assemblies ??= [typeof(string).Assembly];
            WriteVerbose($"{nameof(GenerateNamespaces)} for {assemblies.Count()} assemblies");

            // System.Threading.Tasks.Parallel.ForEach(assemblies, ass => 
            foreach (var ass in assemblies)
            {
                if (this.Host.Version <= new System.Version("7.3") && ass.IsDynamic) {
                    WriteWarning($"Skipping Dynamic Assembly: {ass.FullName}");
                    continue;
                }
                var asns = ass.GetExportedTypes()

                    .Select(t => t.Namespace ?? "")
                    .Distinct()
                    .SelectMany(s => getNsParts(s ?? ""));

                foreach (var ns in asns)
                {
                    if (Stopping) return;
                    if (NamespacesInAssemblies.TryGetValue(ns, out var ns2))
                    {
                        if (!ns2.Contains(ass))
                            ns2.Add(ass);
                    }
                    else
                        NamespacesInAssemblies.Add(ns, [ass]);
                }
                // });
            }

            rootNamespaces = NamespacesInAssemblies.Keys
                    .Where(n => n != "")
                    .Select(n => n.Substring(0, (n.IndexOf('.') == -1) ? n.Length : n.IndexOf('.')))
                    .Distinct()
                    .OrderBy(s => s);
            WriteVerbose($"generateNamespaces took {sw.ElapsedMilliseconds}");
        }
    }

    static IEnumerable<string> getNsParts(string ns)
    {
        // parallel error -> WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {ns}");
        for (int i = 1; i < ns.Length - 1; i++)
            if (ns[i] == '.')
                yield return ns.Substring(0, i);
        yield return ns;
    }

    private void onLoadAssembly(object? sender, AssemblyLoadEventArgs args)
    {
        WriteVerbose($"{nameof(onLoadAssembly)} {args.LoadedAssembly.GetName().Name}");
        GenerateNamespaces([args.LoadedAssembly]);
    }

    private string toNamespacePath(string path)
    {
        string ret = path.Replace('\\', '.').TrimEnd('.');
        WriteDebug($"toNamespacePath {path} -> {ret}");
        return ret;
    }

    #endregion


    protected override bool IsItemContainer(string path)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");
        if (String.IsNullOrEmpty(path))
            return true;
        return NamespacesInAssemblies.ContainsKey(toNamespacePath(path));
    }

    protected override void GetChildItems(string path, bool recurse)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");

        IOrderedEnumerable<string> namespaces;
        if (string.IsNullOrWhiteSpace(path))
            namespaces = rootNamespaces;
        else
            namespaces = NamespacesInAssemblies.Keys.Where(ns => ns.StartsWith(toNamespacePath(path) + ".") && (ns.Count(s => s == '.') == (toNamespacePath(path) + ".").Count(s => s == '.'))).OrderBy(s=>s);
        WriteDebug($"nses: {namespaces}");
        foreach (var ns in namespaces)
        {
            WriteDebug($"ns: {ns}");
            if (Stopping) return;
            WriteItemObject(new NamespaceType(ns), ns.Replace('.', '\\'), true);
        }

        foreach (var item in GetTypes(path))
        {
            if (Stopping) return;
            WriteItemObject(item, item.FullName.Replace('.', '\\'), false);
        }

        // doppelt
        if (recurse)
            foreach (var ns in namespaces)
                GetChildItems(ns, recurse);
    }

    protected override void GetChildNames(string path, ReturnContainers returnContainers)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");

        IOrderedEnumerable<string> namespaces;
        if (string.IsNullOrWhiteSpace(path))
            namespaces = rootNamespaces;
        else
            namespaces = NamespacesInAssemblies.Keys.Where(ns => ns.StartsWith(toNamespacePath(path) + ".") && (ns.Count(s => s == '.') == (toNamespacePath(path) + ".").Count(s => s == '.'))).OrderBy(s=>s);

        foreach (var ns in namespaces)
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

    protected override string GetChildName(string path)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");
        if (path != "")
            return base.GetChildName(path);
        return "";
    }

    IEnumerable<Type> GetTypes(string path)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");
        string ns = toNamespacePath(path);
        if (NamespacesInAssemblies.TryGetValue(ns, out var relAss))
            return new List<Assembly>(relAss)
                .SelectMany(a =>
                {
                    try { return this.Force ? a.GetTypes() : a.GetExportedTypes(); }
                    catch (Exception e)
                    {
                        WriteError(new ErrorRecord(e, "ExportTypes", ErrorCategory.MetadataError, a));
                        return System.Array.Empty<Type>();
                    }
                })
                // .ToArray()
                .Where(t => t.Namespace == ns || ns == "" && t.Namespace == null)
                .OrderBy(t => t.Name);
        else
            return System.Array.Empty<Type>();
    }

    protected override bool ItemExists(string path)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");
        string nsPath = toNamespacePath(path);
        if (String.IsNullOrEmpty(path))
            return true;
        if (NamespacesInAssemblies.ContainsKey(nsPath))
            return true;
        if (FindType(path) != null)
            return true;
        return false;
    }

    // Only Types within same assembly can be found by nameonly
    // Searching the Namespaces Assembly List for that name
    Type FindType(string path)
    {
        string nsPath = toNamespacePath(path);
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");
        Type? retType = Type.GetType(nsPath, false, true);

        // AssemblyQualifiedName is needed for resolve, so search in all assemblies that contain parent NS
        if (retType == null)
        {
            var parentNs = toNamespacePath(GetParentPath(path, ""));
            if (NamespacesInAssemblies.TryGetValue(parentNs, out var asslist))
            {
                retType = asslist
                    .SelectMany(a => a.GetExportedTypes())
                    .FirstOrDefault(t => t.FullName == nsPath);
            }
        }

        return retType;
    }

    protected override void GetItem(string path)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");
        if (string.IsNullOrEmpty(path)) {

        } else if (IsItemContainer(path))
            WriteItemObject(new NamespaceType(path), path, true);
        else
            WriteItemObject(FindType(path), path, false);
    }

    protected override bool HasChildItems(string path)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");
        return NamespacesInAssemblies.ContainsKey(toNamespacePath(path));
    }

    protected override bool IsValidPath(string path)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");
        return true;
    }

    #region Invoke Constructor

    protected override void InvokeDefaultAction(string path)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");
        object[] argArray = [];
        var typ = FindType(path);
        if (DynamicParameters is InvokeDefaultActionDynamicParameter invokeParams)
            argArray = invokeParams.Arguments ?? [];

        var instance = System.Activator.CreateInstance(typ, argArray);

        // WriteItemObject is not the best choice WriteOutput would be better
        this.WriteItemObject(instance, path + ".ctor()", false);
    }

    protected override object InvokeDefaultActionDynamicParameters(string path)
    => new InvokeDefaultActionDynamicParameter();

    #endregion

    #region Property

    static string MemberDefinition(MemberInfo mem)
    {
        // WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {mem.Name}");
        var sb = new StringBuilder();
        if (mem is MethodInfo metInfo)
        {
            sb.Append(GetTypeAccelerated(metInfo.ReturnType));
            sb.Append(' ');
            sb.Append(mem.Name);
            sb.Append('(');
            sb.Append(String.Join(",", metInfo.GetParameters()
                .Select(p => $"{GetTypeAccelerated(p.ParameterType)} {p.Name}")));
            sb.Append(')');
        }
        else if (mem is PropertyInfo propInfo)
        {
            sb.Append(GetTypeAccelerated(propInfo.PropertyType));
            sb.Append(' ');
            sb.Append(mem.Name);
            sb.Append(" {");
            if (propInfo.CanRead) sb.Append("get;");
            if (propInfo.CanWrite) sb.Append("set;");
            sb.Append('}');
        }
        else if (mem is System.Reflection.EventInfo eventInfo)
        {
            sb.Append("event ");
            sb.Append(eventInfo.EventHandlerType);
            sb.Append(' ');
            sb.Append(mem.Name);
            sb.Append('(');
            // sb.Append(String.Join(',', eventInfo.g
            //     .Select(p => $"{GetTypeAccelerated(p.ParameterType)} {p.Name}")));
            sb.Append(')');
        }
        else if (mem is ConstructorInfo conInfo)
        {
            sb.Append("new(");
            sb.Append(String.Join(",", conInfo.GetParameters()
                .Select(p => $"{GetTypeAccelerated(p.ParameterType)} {p.Name}")));
            sb.Append(')');
        }
        else
        {
            sb.Append(mem.MemberType);
            sb.Append(' ');
            sb.Append(mem.Name);
        }
        return sb.ToString();
    }

    static Lazy<Dictionary<Type, string>> TypeAccelerators = new(() =>
    {
        Type TypeAccelerators = typeof(PSObject).Assembly.GetType("System.Management.Automation.TypeAccelerators", false);
        var GetProp = TypeAccelerators?.GetProperty("Get");
        var getgetMethod = GetProp?.GetGetMethod();
        var dicUnknown = getgetMethod?.Invoke(null, null);
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

    static string GetTypeAccelerated(Type typ)
    {
        //WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {typ}");
        if (TypeAccelerators.Value.TryGetValue(typ, out var retstring))
            return retstring;
        else
            return typ.ToString();
    }

    public void GetProperty(string path, Collection<string>? providerSpecificPickList)
    {
        WriteDebug($"{MethodBase.GetCurrentMethod()?.Name} {path}");
        if (!IsItemContainer(path))
        {
            var getPropertyDynamic = DynamicParameters as GetPropertyDynamicParameter;

            var t = FindType(path);

            var propertyResults = new PSObject();
            var dicProps = new Dictionary<string, List<object>>();

            // Defaults to members
            if (getPropertyDynamic?.Attributes != true && getPropertyDynamic?.Interfaces != true && getPropertyDynamic?.EnumValues != true
            || getPropertyDynamic?.Members == true)
            {
                foreach (var mem in t.GetMembers().OrderBy(m => m.Name))
                {
                    if (Stopping) return;
                    if (!mem.Name.StartsWith("get_") && !mem.Name.StartsWith("set_") && !(mem is MethodInfo mi && mi.IsSpecialName))
                    {
                        if (dicProps.TryGetValue(mem.Name, out var defs))
                            defs.Add(MemberDefinition(mem));
                        else
                            dicProps.Add(mem.Name, [MemberDefinition(mem)]);
                    }
                }
            }
            if (getPropertyDynamic?.Interfaces == true)
            {
                foreach (var iface in t.GetInterfaces().OrderBy(m => m.Name))
                {
                    if (Stopping) return;
                    dicProps.Add(iface.Name, iface.GetMembers().Select(m => MemberDefinition(m)).ToList<object>());
                }
            }
            if (getPropertyDynamic?.Attributes == true)
            {
                foreach (var att in t.GetCustomAttributes()/* .OrderBy(m => m.TypeId ?? "") */)
                {
                    if (Stopping) return;
                    dicProps.Add(att.TypeId?.ToString() ?? "unknownAtttribut", [att]);
                }
            }
            if (getPropertyDynamic?.EnumValues == true)
            {
                foreach (var enumitem in t.GetEnumValues())
                {
                    if (Stopping) return;
                    dicProps.Add(enumitem.ToString()!, [(int)enumitem]);
                }
            }


            foreach (var mem in dicProps.Where(m => providerSpecificPickList == null
                                        || providerSpecificPickList.Count == 0
                                        || providerSpecificPickList.Contains("*")
                                        || providerSpecificPickList.Contains(m.Key)))
                propertyResults.Properties.Add(new PSNoteProperty(mem.Key, mem.Value));
            if (propertyResults.Properties.Count() > 0)
                WritePropertyObject(propertyResults, path);
        }
    }

    public object? GetPropertyDynamicParameters(string path, Collection<string>? providerSpecificPickList)
        => new GetPropertyDynamicParameter();

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

    internal class InvokeDefaultActionDynamicParameter
    {
        /// <summary>
        /// Constructor Arguments
        /// </summary>
        [Parameter(Position = 0, HelpMessage = "Constructor Arguments")]
        public object[]? Arguments { get; set; }
    }

    internal class GetPropertyDynamicParameter
    {

        [Parameter]
        public SwitchParameter Members { get; set; }

        [Parameter]
        public SwitchParameter Interfaces { get; set; }

        [Parameter]
        public SwitchParameter Attributes { get; set; }

        [Parameter]
        public SwitchParameter EnumValues { get; set; }

    }
}
