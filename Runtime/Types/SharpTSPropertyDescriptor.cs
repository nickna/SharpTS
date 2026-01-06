namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a JavaScript-style property descriptor for method/accessor decorators.
/// Used in Legacy (Stage 2) decorators for method and accessor decoration.
/// </summary>
public class SharpTSPropertyDescriptor
{
    /// <summary>Value for data properties (the method function)</summary>
    public object? Value { get; set; }

    /// <summary>Getter function for accessor properties</summary>
    public ISharpTSCallable? Get { get; set; }

    /// <summary>Setter function for accessor properties</summary>
    public ISharpTSCallable? Set { get; set; }

    /// <summary>Whether the property value can be changed</summary>
    public bool Writable { get; set; } = true;

    /// <summary>Whether the property shows up in enumeration</summary>
    public bool Enumerable { get; set; } = false;

    /// <summary>Whether the property can be deleted or changed</summary>
    public bool Configurable { get; set; } = true;

    public SharpTSPropertyDescriptor() { }

    public SharpTSPropertyDescriptor(
        object? value = null,
        ISharpTSCallable? getter = null,
        ISharpTSCallable? setter = null,
        bool writable = true,
        bool enumerable = false,
        bool configurable = true)
    {
        Value = value;
        Get = getter;
        Set = setter;
        Writable = writable;
        Enumerable = enumerable;
        Configurable = configurable;
    }

    /// <summary>
    /// Converts this descriptor to a SharpTS object for passing to decorators.
    /// </summary>
    public SharpTSObject ToObject()
    {
        var obj = new SharpTSObject(new Dictionary<string, object?>());

        if (Value != null)
        {
            obj.Set("value", Value);
            obj.Set("writable", Writable);
        }

        if (Get != null)
        {
            obj.Set("get", Get);
        }

        if (Set != null)
        {
            obj.Set("set", Set);
        }

        obj.Set("enumerable", Enumerable);
        obj.Set("configurable", Configurable);

        return obj;
    }

    /// <summary>
    /// Creates a PropertyDescriptor from a SharpTS object returned by a decorator.
    /// </summary>
    public static SharpTSPropertyDescriptor FromObject(SharpTSObject obj)
    {
        var descriptor = new SharpTSPropertyDescriptor();

        var value = obj.Get("value");
        if (value != null)
        {
            descriptor.Value = value;
        }

        var getter = obj.Get("get");
        if (getter is ISharpTSCallable getterFn)
        {
            descriptor.Get = getterFn;
        }

        var setter = obj.Get("set");
        if (setter is ISharpTSCallable setterFn)
        {
            descriptor.Set = setterFn;
        }

        var writable = obj.Get("writable");
        if (writable is bool w)
        {
            descriptor.Writable = w;
        }

        var enumerable = obj.Get("enumerable");
        if (enumerable is bool e)
        {
            descriptor.Enumerable = e;
        }

        var configurable = obj.Get("configurable");
        if (configurable is bool c)
        {
            descriptor.Configurable = c;
        }

        return descriptor;
    }

    /// <summary>
    /// Creates a descriptor for a method.
    /// </summary>
    public static SharpTSPropertyDescriptor ForMethod(ISharpTSCallable method)
    {
        return new SharpTSPropertyDescriptor(
            value: method,
            writable: true,
            enumerable: false,
            configurable: true
        );
    }

    /// <summary>
    /// Creates a descriptor for a getter.
    /// </summary>
    public static SharpTSPropertyDescriptor ForGetter(ISharpTSCallable getter)
    {
        return new SharpTSPropertyDescriptor(
            getter: getter,
            enumerable: false,
            configurable: true
        );
    }

    /// <summary>
    /// Creates a descriptor for a setter.
    /// </summary>
    public static SharpTSPropertyDescriptor ForSetter(ISharpTSCallable setter)
    {
        return new SharpTSPropertyDescriptor(
            setter: setter,
            enumerable: false,
            configurable: true
        );
    }

    /// <summary>
    /// Creates a descriptor for an accessor pair.
    /// </summary>
    public static SharpTSPropertyDescriptor ForAccessor(ISharpTSCallable? getter, ISharpTSCallable? setter)
    {
        return new SharpTSPropertyDescriptor(
            getter: getter,
            setter: setter,
            enumerable: false,
            configurable: true
        );
    }
}
