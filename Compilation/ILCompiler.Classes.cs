using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Class definition and method emission for the IL compiler.
///
/// This partial class is split across multiple files:
/// - ILCompiler.Classes.cs: Core class definition (DefineClass)
/// - ILCompiler.Classes.Properties.cs: Property/field handling
/// - ILCompiler.Classes.Methods.cs: Instance method definition and emission
/// - ILCompiler.Classes.Static.cs: Static methods and static constructor
/// - ILCompiler.Classes.Constructors.cs: Constructor emission
/// - ILCompiler.Classes.Accessors.cs: Getter/setter accessors
/// </summary>
public partial class ILCompiler
{
    private void DefineClass(Stmt.Class classStmt)
    {
        var ctx = GetDefinitionContext();

        // Get qualified class name (includes module prefix and .NET namespace if set)
        string qualifiedClassName = ctx.GetQualifiedClassName(classStmt.Name.Lexeme);

        // Track simple name -> module mapping for later lookups
        if (_currentModulePath != null)
        {
            _classToModule[classStmt.Name.Lexeme] = _currentModulePath;
        }

        Type? baseType = null;
        string? qualifiedSuperclassName = null;
        if (classStmt.Superclass != null)
        {
            // Resolve superclass name (includes module prefix and .NET namespace if set)
            qualifiedSuperclassName = ctx.ResolveClassName(classStmt.Superclass.Lexeme);

            if (_classBuilders.TryGetValue(qualifiedSuperclassName, out var superBuilder))
            {
                baseType = superBuilder;
            }
        }

        // Set TypeAttributes.Abstract if the class is abstract
        TypeAttributes typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (classStmt.IsAbstract)
        {
            typeAttrs |= TypeAttributes.Abstract;
        }

        var typeBuilder = _moduleBuilder.DefineType(
            qualifiedClassName,
            typeAttrs,
            baseType
        );

        // Track superclass for inheritance-aware method resolution
        _classSuperclass[qualifiedClassName] = qualifiedSuperclassName;

        // Handle generic type parameters
        if (classStmt.TypeParams != null && classStmt.TypeParams.Count > 0)
        {
            string[] typeParamNames = classStmt.TypeParams.Select(tp => tp.Name.Lexeme).ToArray();
            var genericParams = typeBuilder.DefineGenericParameters(typeParamNames);

            // Apply constraints
            for (int i = 0; i < classStmt.TypeParams.Count; i++)
            {
                var constraint = classStmt.TypeParams[i].Constraint;
                if (constraint != null)
                {
                    Type constraintType = ResolveConstraintType(constraint);
                    if (constraintType.IsInterface)
                        genericParams[i].SetInterfaceConstraints(constraintType);
                    else
                        genericParams[i].SetBaseTypeConstraint(constraintType);
                }
            }

            _classGenericParams[qualifiedClassName] = genericParams;
        }

        string className = qualifiedClassName;

        // Initialize property tracking dictionaries for this class
        _propertyBackingFields[className] = [];
        _classProperties[className] = [];
        _declaredPropertyNames[className] = [];
        _readonlyPropertyNames[className] = [];
        _propertyTypes[className] = [];

        // Add _fields dictionary for dynamic property storage
        // Note: We keep this as _fields for now to maintain compatibility with RuntimeEmitter.Objects.cs
        // In Phase 4, both this and the runtime will be updated to use _extras
        var fieldsField = typeBuilder.DefineField(
            "_fields",
            typeof(Dictionary<string, object>),
            FieldAttributes.Private
        );
        _extrasFields[className] = fieldsField;
        _instanceFieldsField[className] = fieldsField;

        // Get class generic params if any
        _classGenericParams.TryGetValue(className, out var classGenericParams);

        // Define real .NET properties with typed backing fields for instance fields
        // Skip fields with generic type parameters - they'll use _extras dictionary instead
        foreach (var field in classStmt.Fields)
        {
            if (!field.IsStatic)
            {
                // Check if field type is a generic parameter
                bool isGenericField = classGenericParams != null &&
                    field.TypeAnnotation != null &&
                    classGenericParams.Any(p => p.Name == field.TypeAnnotation);

                if (!isGenericField)
                {
                    DefineInstanceProperty(typeBuilder, className, field, classGenericParams);
                }
            }
        }

        // Add static fields for static properties (use object type for backward compatibility)
        Dictionary<string, FieldBuilder> staticFieldBuilders = [];
        foreach (var field in classStmt.Fields)
        {
            if (field.IsStatic)
            {
                // Keep as object type for now to maintain compatibility with existing emission code
                var fieldBuilder = typeBuilder.DefineField(
                    field.Name.Lexeme,
                    typeof(object),
                    FieldAttributes.Public | FieldAttributes.Static
                );
                staticFieldBuilders[field.Name.Lexeme] = fieldBuilder;
            }
        }

        _classBuilders[className] = typeBuilder;
        _staticFields[className] = staticFieldBuilders;

        // Apply class-level decorators as .NET attributes
        if (_decoratorMode != DecoratorMode.None)
        {
            ApplyClassDecorators(classStmt, typeBuilder);
        }
    }
}
