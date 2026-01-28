namespace SharpTS.TypeSystem;

/// <summary>
/// Unified categorization of TypeScript/runtime types for property dispatch.
/// Used by TypeChecker, Interpreter, and ILEmitter to share type classification logic.
/// </summary>
public enum TypeCategory
{
    // Primitives
    String,
    Number,
    Boolean,
    BigInt,
    Symbol,

    // Built-in object types
    Array,
    Tuple,
    Map,
    Set,
    WeakMap,
    WeakSet,
    Date,
    RegExp,
    Error,
    Promise,
    Timeout,
    Iterator,
    Generator,
    AsyncGenerator,
    Buffer,
    EventEmitter,

    // User-defined types
    Class,          // Static access on class constructor (Foo.staticProp)
    Instance,       // Instance of a class (new Foo().prop)
    Interface,      // Interface type
    Record,         // Object literal / record type
    Enum,           // Enum type
    Namespace,      // Namespace type

    // Special types
    TypeParameter,  // Generic type parameter (T)
    Union,          // Union type (A | B)
    Intersection,   // Intersection type (A & B)
    Function,       // Function type
    Any,            // any type
    Unknown,        // unknown type
    Never,          // never type
    Void,           // void type
    Null,           // null type
    Undefined,      // undefined type
    External        // External .NET type (@DotNetType)
}
