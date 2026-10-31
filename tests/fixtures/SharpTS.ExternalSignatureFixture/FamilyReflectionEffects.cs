using System.Reflection;

namespace ExternalSignatureFixture;

public static class FamilyReflectionEffects
{
    private static int _count = 0;

    public static double Touch()
    {
        var field = typeof(FamilyReflectionEffects).GetField(nameof(_count), BindingFlags.NonPublic | BindingFlags.Static)!;
        field.SetValue(null, (int)field.GetValue(null)! + 1);
        return (int)field.GetValue(null)!;
    }
}
