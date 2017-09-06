using System.Runtime.CompilerServices;

public class PdbOnly
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override string ToString() => throw new System.Exception("this is a test exception");
}