using System.Runtime.CompilerServices;

public class Full
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public override string ToString() => throw new System.Exception("this is a test exception");
}