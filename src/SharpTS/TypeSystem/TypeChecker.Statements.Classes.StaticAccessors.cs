using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private static void PublishStaticAccessorTypes(TypeInfo.MutableClass mutableClass)
    {
        foreach (string name in mutableClass.StaticGetters.Keys.Union(mutableClass.StaticSetters.Keys))
        {
            mutableClass.StaticProperties[name] = mutableClass.StaticGetters.GetValueOrDefault(name)
                ?? mutableClass.StaticSetters[name];
            if (mutableClass.StaticGetters.ContainsKey(name) && !mutableClass.StaticSetters.ContainsKey(name))
                mutableClass.StaticReadonlyFields.Add(name);
            else
                mutableClass.StaticReadonlyFields.Remove(name);
        }
    }

    private void ValidateStaticAccessorPairs(TypeInfo.MutableClass mutableClass)
    {
        foreach (string name in mutableClass.StaticGetters.Keys.Intersect(mutableClass.StaticSetters.Keys))
        {
            TypeInfo getterType = mutableClass.StaticGetters[name];
            TypeInfo setterType = mutableClass.StaticSetters[name];
            if (setterType is TypeInfo.Inferred && getterType is not TypeInfo.Inferred)
                mutableClass.StaticSetters[name] = getterType;
        }
        PublishStaticAccessorTypes(mutableClass);
    }
}
