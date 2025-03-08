using System.Collections.Generic;

namespace Serpen.PS;

public class NamespaceType
{
    internal NamespaceType(string ns) => FullName = ns;

    public readonly string FullName;
    
    public string Name
    {
        get
        {
            if (!FullName.Contains('.'))
                return FullName;
            else
                return FullName.Substring(FullName.LastIndexOf('.') + 1);
        }
    }

    public string Namespace
    {
        get
        {
            if (!FullName.Contains('.'))
                return null;
            else
                return FullName.Substring(0, FullName.LastIndexOf('.'));
        }
    }
    public string TypeType { get; } = "Namespace";

    public IEnumerable<System.Reflection.Assembly> Assembly
        => TypeProvider.NamespacesInAssemblies[FullName];

    public override string ToString() => FullName;
}