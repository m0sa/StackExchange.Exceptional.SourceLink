using System.Runtime.CompilerServices;

public class Portable
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override string ToString() => throw new System.Exception("this is a test exception");
}