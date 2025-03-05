
public class NamespaceType
{
    internal NamespaceType(string ns)
    {
        FullName = ns;
    }

    public readonly string FullName;
    public string Name
    {
        get
        {
            if (!FullName.Contains('.'))
                return FullName;
            else
                return FullName.Substring(FullName.LastIndexOf('.')+1);
        }
    }

    public string Namespace
    {
        get
        {
            if (!FullName.Contains('.'))
                return "";
            else
                return FullName.Substring(0, FullName.LastIndexOf('.'));
        }
    }
    public string MemberType { get; } = "NamespaceInfo";

    public override string ToString()
    {
        return FullName;
    }
}