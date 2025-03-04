
public class Namespace
{
    internal Namespace(string ns) {
        Name = ns;
    }

    public string Name { get; }

    public override string ToString()
    {
        return Name;
    }
}