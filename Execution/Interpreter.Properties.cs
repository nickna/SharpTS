using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    /// <summary>
    /// Evaluates a <c>new</c> expression, instantiating a class.
    /// </summary>
    /// <param name="newExpr">The new expression AST node.</param>
    /// <returns>A new <see cref="SharpTSInstance"/> of the class.</returns>
    /// <remarks>
    /// Looks up the class by name, evaluates constructor arguments,
    /// and invokes the class's <see cref="SharpTSClass.Call"/> method.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/classes.html#constructors">TypeScript Constructors</seealso>
    private object? EvaluateNew(Expr.New newExpr)
    {
        object? klass = _environment.Get(newExpr.ClassName);
        if (klass is not SharpTSClass sharpClass)
        {
             throw new Exception("Type Error: Can only instantiate classes.");
        }

        List<object?> arguments = [];
        foreach (Expr argument in newExpr.Arguments)
        {
            arguments.Add(Evaluate(argument));
        }

        return sharpClass.Call(this, arguments);
    }

    /// <summary>
    /// Evaluates a <c>this</c> expression, returning the current instance.
    /// </summary>
    /// <param name="expr">The this expression AST node.</param>
    /// <returns>The current class instance bound to <c>this</c>.</returns>
    /// <remarks>
    /// The <c>this</c> keyword is bound in the environment when a method is called
    /// on an instance.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/classes.html#this-at-runtime-in-classes">TypeScript this in Classes</seealso>
    private object? EvaluateThis(Expr.This expr)
    {
        return _environment.Get(expr.Keyword);
    }

    /// <summary>
    /// Evaluates a property access expression (dot notation).
    /// </summary>
    /// <param name="get">The property access expression AST node.</param>
    /// <returns>The value of the property, or a bound method.</returns>
    /// <remarks>
    /// Handles optional chaining (<c>?.</c>), static member access on classes,
    /// enum member access, instance properties/methods, object properties,
    /// string methods, array methods, and Math object members.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/objects.html">TypeScript Object Types</seealso>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/release-notes/typescript-3-7.html#optional-chaining">TypeScript Optional Chaining</seealso>
    private object? EvaluateGet(Expr.Get get)
    {
        object? obj = Evaluate(get.Object);

        // Handle optional chaining - return null if object is null
        if (get.Optional && obj == null)
        {
            return null;
        }

        // Handle static member access on class
        if (obj is SharpTSClass klass)
        {
            // Try static method first
            SharpTSFunction? staticMethod = klass.FindStaticMethod(get.Name.Lexeme);
            if (staticMethod != null) return staticMethod;

            // Try static property
            if (klass.HasStaticProperty(get.Name.Lexeme))
            {
                return klass.GetStaticProperty(get.Name.Lexeme);
            }

            throw new Exception($"Runtime Error: Static member '{get.Name.Lexeme}' does not exist on class '{klass.Name}'.");
        }

        // Handle enum member access
        if (obj is SharpTSEnum enumObj)
        {
            return enumObj.GetMember(get.Name.Lexeme);
        }

        if (obj is SharpTSInstance instance)
        {
            instance.SetInterpreter(this);
            return instance.Get(get.Name);
        }
        if (obj is SharpTSObject simpleObj)
        {
            return simpleObj.Get(get.Name.Lexeme);
        }
        // Handle strings
        if (obj is string str)
        {
            var member = StringBuiltIns.GetMember(str, get.Name.Lexeme);
            if (member is BuiltInMethod m) return m.Bind(str);
            if (member != null) return member;
            throw new Exception($"Runtime Error: Property '{get.Name.Lexeme}' does not exist on string.");
        }
        // Handle arrays
        if (obj is SharpTSArray array)
        {
            var member = ArrayBuiltIns.GetMember(array, get.Name.Lexeme);
            if (member is BuiltInMethod m) return m.Bind(array);
            if (member != null) return member;
            throw new Exception($"Runtime Error: Property '{get.Name.Lexeme}' does not exist on array.");
        }
        // Handle Math object
        if (obj is SharpTSMath)
        {
            var member = MathBuiltIns.GetMember(get.Name.Lexeme);
            if (member != null) return member;
            throw new Exception($"Runtime Error: Property '{get.Name.Lexeme}' does not exist on Math.");
        }

        throw new Exception("Only instances and objects have properties.");
    }

    /// <summary>
    /// Evaluates a property assignment expression (dot notation with assignment).
    /// </summary>
    /// <param name="set">The property assignment expression AST node.</param>
    /// <returns>The assigned value.</returns>
    /// <remarks>
    /// Supports static property assignment on classes, instance field assignment,
    /// and simple object property assignment.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/objects.html">TypeScript Object Types</seealso>
    private object? EvaluateSet(Expr.Set set)
    {
        object? obj = Evaluate(set.Object);

        // Handle static property assignment
        if (obj is SharpTSClass klass)
        {
            object? value = Evaluate(set.Value);
            klass.SetStaticProperty(set.Name.Lexeme, value);
            return value;
        }

        if (obj is SharpTSInstance instance)
        {
            instance.SetInterpreter(this);
            object? value = Evaluate(set.Value);
            instance.Set(set.Name, value);
            return value;
        }
        if (obj is SharpTSObject simpleObj)
        {
            object? value = Evaluate(set.Value);
            simpleObj.Set(set.Name.Lexeme, value);
            return value;
        }

        throw new Exception("Only instances and objects have fields.");
    }

    /// <summary>
    /// Evaluates a variable assignment expression.
    /// </summary>
    /// <param name="assign">The assignment expression AST node.</param>
    /// <returns>The assigned value.</returns>
    /// <remarks>
    /// Evaluates the right-hand side value and updates the variable
    /// in the current <see cref="RuntimeEnvironment"/>.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/variable-declarations.html">TypeScript Variable Declarations</seealso>
    private object? EvaluateAssign(Expr.Assign assign)
    {
        object? value = Evaluate(assign.Value);
        _environment.Assign(assign.Name, value);
        return value;
    }
}
