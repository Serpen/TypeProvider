
public class Namespace
{
    internal Namespace(string ns)
    {
        Name = ns;
    }

    public string Name { get; }

    public string MemberType { get; } = "NamespaceInfo";

    public override string ToString()
    {
        return Name;
    }
}