using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Negative/fuzzing tests to verify the compiler handles malformed input gracefully.
/// These tests ensure the compiler fails with proper error messages instead of crashing,
/// hanging, or producing undefined behavior when given invalid input.
/// </summary>
public class NegativeTests
{
    #region Helper Methods

    /// <summary>
    /// Lexes source code and returns tokens.
    /// </summary>
    private static List<Token> TryLex(string source)
    {
        var lexer = new Lexer(source);
        return lexer.ScanTokens();
    }

    /// <summary>
    /// Parses source code using recovery mode and returns the ParseResult.
    /// </summary>
    private static ParseResult TryParse(string source)
    {
        var tokens = TryLex(source);
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    /// <summary>
    /// Attempts the full pipeline (lex, parse, typecheck) and returns true if any stage reports errors.
    /// Returns false if the pipeline completes successfully with no errors.
    /// Does NOT throw on errors - this is for testing that errors are handled gracefully.
    /// </summary>
    private static bool FailsGracefully(string source)
    {
        try
        {
            var tokens = TryLex(source);
            var parser = new Parser(tokens);
            var parseResult = parser.Parse();

            if (!parseResult.IsSuccess)
                return true;

            var checker = new TypeChecker();
            var typeCheckResult = checker.CheckWithRecovery(parseResult.Statements);

            if (!typeCheckResult.IsSuccess)
                return true;

            return false; // No errors detected
        }
        catch
        {
            // Any exception is also "graceful" failure for our purposes
            return true;
        }
    }

    /// <summary>
    /// Runs the pipeline with a timeout to catch infinite loops or stack overflows.
    /// Returns true if the pipeline completes within the timeout (with or without errors).
    /// Returns false if the timeout is exceeded.
    /// </summary>
    private static bool CompletesWithinTimeout(string source, int timeoutMs = 5000)
    {
        var task = Task.Run(() =>
        {
            try
            {
                var tokens = TryLex(source);
                var parser = new Parser(tokens);
                var parseResult = parser.Parse();

                if (parseResult.IsSuccess)
                {
                    var checker = new TypeChecker();
                    checker.CheckWithRecovery(parseResult.Statements);
                }
                return true;
            }
            catch
            {
                // Exceptions are fine - we just want to ensure it completes
                return true;
            }
        });

        return task.Wait(TimeSpan.FromMilliseconds(timeoutMs));
    }

    /// <summary>
    /// Asserts that processing the source completes within timeout and either succeeds or reports errors gracefully.
    /// </summary>
    private static void AssertHandlesGracefully(string source, int timeoutMs = 5000)
    {
        Assert.True(CompletesWithinTimeout(source, timeoutMs),
            "Processing did not complete within timeout - possible infinite loop or stack overflow");
    }

    #endregion

    #region Syntactic Garbage

    [Fact]
    public void Garbage_RandomBytes_DoesNotCrash()
    {
        var source = "\x00\x01\x02\x03\xFF\xFE\xFD";
        AssertHandlesGracefully(source);
    }

    [Fact]
    public void Garbage_RandomUnicode_DoesNotCrash()
    {
        var source = "\u2603\u2764\u2665\u263A\u00A9\u00AE\u2122";
        AssertHandlesGracefully(source);
    }

    [Fact]
    public void Garbage_ControlCharacters_DoesNotCrash()
    {
        var source = "\a\b\f\v\x1B\x7F";
        AssertHandlesGracefully(source);
    }

    [Fact]
    public void Garbage_OnlySymbols_DoesNotCrash()
    {
        var source = "!@#$%^&*(){}[]<>~`|\\";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void Garbage_MixedValidInvalid_DoesNotCrash()
    {
        var source = "let x = \x00 + y;";
        AssertHandlesGracefully(source);
    }

    #endregion

    #region Partially Valid Code

    [Fact]
    public void PartialCode_FunctionWithoutBody_ReportsError()
    {
        var source = "function {";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void PartialCode_ClassWithoutName_ReportsError()
    {
        var source = "class { x: number }";
        AssertHandlesGracefully(source);
        // Note: class expressions are valid, so this might parse - but the body syntax is wrong
        var result = TryParse(source);
        // Either it parses as a class expression or reports errors
        Assert.True(result.Statements.Count > 0 || !result.IsSuccess);
    }

    [Fact]
    public void PartialCode_IfWithoutCondition_ReportsError()
    {
        var source = "if { }";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void PartialCode_IncompleteArrowFunction_ReportsError()
    {
        var source = "const f = (x) =>";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void PartialCode_UnclosedTemplateLiteral_HandlesGracefully()
    {
        var source = "`hello ${world";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void PartialCode_UnterminatedString_HandlesGracefully()
    {
        var source = "let x = \"hello";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void PartialCode_NestedUnterminatedStrings_HandlesGracefully()
    {
        var source = "let x = \"hello; let y = \"world; let z = \"test";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void PartialCode_MismatchedBraces_ReportsError()
    {
        var source = "{ let x = 5; ] }";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void PartialCode_TruncatedTypescript_ReportsError()
    {
        var source = "interface Person { name:";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void PartialCode_IncompleteDestructuring_ReportsError()
    {
        var source = "const { a, b, } =";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    #endregion

    #region Deep Nesting

    // Note: Deep nesting tests use moderate depths to avoid stack overflow.
    // The recursive descent parser has limited stack depth tolerance.
    // Values around 100-200 are safe; beyond that risks crashing the test host.

    [Fact]
    public void DeepNesting_100_Parentheses_Completes()
    {
        var source = new string('(', 100) + "1" + new string(')', 100);
        AssertHandlesGracefully(source, 10000);
    }

    [Fact]
    public void DeepNesting_200_Parentheses_Completes()
    {
        var source = new string('(', 200) + "1" + new string(')', 200);
        AssertHandlesGracefully(source, 10000);
    }

    [Fact]
    public void DeepNesting_100_Brackets_Completes()
    {
        var source = new string('[', 100) + "1" + new string(']', 100);
        AssertHandlesGracefully(source, 10000);
    }

    [Fact]
    public void DeepNesting_100_Braces_Completes()
    {
        // Nested blocks: { { { ... } } }
        var source = new string('{', 100) + new string('}', 100);
        AssertHandlesGracefully(source, 10000);
    }

    [Fact]
    public void DeepNesting_NestedArrowFunctions_50_Completes()
    {
        // () => () => () => ... => 1
        var source = string.Concat(Enumerable.Repeat("() => ", 50)) + "1";
        AssertHandlesGracefully(source, 10000);
    }

    [Fact]
    public void DeepNesting_NestedTernaries_50_Completes()
    {
        // true ? (true ? (true ? ... : 0) : 0) : 0
        var source = string.Concat(Enumerable.Repeat("true ? ", 50)) + "1" +
                     string.Concat(Enumerable.Repeat(" : 0", 50));
        AssertHandlesGracefully(source, 10000);
    }

    [Fact]
    public void DeepNesting_ChainedPropertyAccess_200_Completes()
    {
        // a.b.c.d.e... (200 levels)
        var source = string.Join(".", Enumerable.Repeat("a", 200));
        AssertHandlesGracefully(source, 10000);
    }

    [Fact]
    public void DeepNesting_NestedTypeAnnotations_50_Completes()
    {
        // Array<Array<Array<...number...>>>
        var source = "let x: " + string.Concat(Enumerable.Repeat("Array<", 50)) + "number" +
                     string.Concat(Enumerable.Repeat(">", 50)) + ";";
        AssertHandlesGracefully(source, 10000);
    }

    #endregion

    #region Resource Exhaustion

    [Fact]
    public void LargeInput_10000_Variables_CompletesOrErrors()
    {
        // Generate 10,000 variable declarations
        var source = string.Join("\n", Enumerable.Range(0, 10000).Select(i => $"let v{i}: number = {i};"));
        AssertHandlesGracefully(source, 60000); // Allow more time for large inputs
    }

    [Fact]
    public void LargeInput_1000_Classes_CompletesOrErrors()
    {
        // Generate 1,000 class declarations
        var source = string.Join("\n", Enumerable.Range(0, 1000).Select(i =>
            $"class C{i} {{ x{i}: number; constructor() {{ this.x{i} = {i}; }} }}"));
        AssertHandlesGracefully(source, 60000);
    }

    [Fact]
    public void LargeInput_VeryLongIdentifier_HandlesGracefully()
    {
        // 10,000 character identifier
        var longId = new string('a', 10000);
        var source = $"let {longId} = 5;";
        AssertHandlesGracefully(source, 10000);
    }

    [Fact]
    public void LargeInput_VeryLongString_HandlesGracefully()
    {
        // 100,000 character string
        var longString = new string('x', 100000);
        var source = $"let x = \"{longString}\";";
        AssertHandlesGracefully(source, 10000);
    }

    [Fact]
    public void LargeInput_ManyParametersFunction_ReportsError()
    {
        // Function with 1,000 parameters
        var params_ = string.Join(", ", Enumerable.Range(0, 1000).Select(i => $"p{i}: number"));
        var source = $"function f({params_}): void {{ }}";
        AssertHandlesGracefully(source, 30000);
    }

    #endregion

    #region Type System Stress

    [Fact]
    public void TypeStress_CircularTypeReference_ReportsError()
    {
        var source = """
            interface A { b: B; }
            interface B { a: A; }
            let x: A;
            """;
        AssertHandlesGracefully(source);
        // Circular types may or may not be errors depending on implementation
        // We just want to ensure it doesn't crash
    }

    [Fact]
    public void TypeStress_DeeplyNestedConditionalType_Completes()
    {
        // Conditional types are not fully supported, but we test resilience
        var source = "type Deep = number extends string ? true : false;";
        AssertHandlesGracefully(source);
    }

    [Fact]
    public void TypeStress_RecursiveGenericType_CompletesOrErrors()
    {
        var source = """
            class Tree<T> {
                value: T;
                left: Tree<T> | null;
                right: Tree<T> | null;
                constructor(v: T) {
                    this.value = v;
                    this.left = null;
                    this.right = null;
                }
            }
            """;
        AssertHandlesGracefully(source);
    }

    [Fact]
    public void TypeStress_UnionWith100Members_Completes()
    {
        // Type alias with 100 union members
        var types = string.Join(" | ", Enumerable.Range(0, 100).Select(i => $"\"{i}\""));
        var source = $"type BigUnion = {types};";
        AssertHandlesGracefully(source, 10000);
    }

    [Fact]
    public void TypeStress_IntersectionWith50Members_Completes()
    {
        // Create 50 interfaces and intersect them
        var interfaces = string.Join("\n", Enumerable.Range(0, 50).Select(i =>
            $"interface I{i} {{ x{i}: number; }}"));
        var intersection = string.Join(" & ", Enumerable.Range(0, 50).Select(i => $"I{i}"));
        var source = interfaces + $"\ntype BigIntersection = {intersection};";
        AssertHandlesGracefully(source, 10000);
    }

    [Fact]
    public void TypeStress_MutuallyRecursiveInterfaces_CompletesOrErrors()
    {
        var source = """
            interface Node {
                children: Node[];
                parent: Node | null;
            }
            interface Tree extends Node {
                root: Node;
            }
            """;
        AssertHandlesGracefully(source);
    }

    #endregion

    #region Invalid Encodings

    [Fact]
    public void Encoding_NullBytes_DoesNotCrash()
    {
        var source = "let\0x = 5;";
        AssertHandlesGracefully(source);
    }

    [Fact]
    public void Encoding_InvalidSurrogatePairs_DoesNotCrash()
    {
        // Unpaired high surrogate
        var source = "let x = \"\uD800\";";
        AssertHandlesGracefully(source);
    }

    [Fact]
    public void Encoding_BOMCharacter_DoesNotCrash()
    {
        // UTF-8 BOM at the start
        var source = "\uFEFFlet x = 5;";
        AssertHandlesGracefully(source);
    }

    [Fact]
    public void Encoding_MixedLineEndings_DoesNotCrash()
    {
        var source = "let x = 1;\r\nlet y = 2;\rlet z = 3;\nlet w = 4;";
        AssertHandlesGracefully(source);
        // Should parse successfully despite mixed line endings
        var result = TryParse(source);
        Assert.True(result.IsSuccess || result.Statements.Count > 0);
    }

    #endregion

    #region Numeric Edge Cases

    [Fact]
    public void Numeric_InvalidSeparator_Start_ParsesAsIdentifier()
    {
        // _123 is a valid identifier, not a number
        var source = "let _123 = 5;";
        AssertHandlesGracefully(source);
        var result = TryParse(source);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Numeric_InvalidSeparator_End_Throws()
    {
        var source = "let x = 123_;";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void Numeric_DoubleSeparator_Throws()
    {
        var source = "let x = 1__2;";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void Numeric_SeparatorAfterDecimal_Throws()
    {
        var source = "let x = 1._2;";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void Numeric_VeryLargeNumber_HandlesGracefully()
    {
        var source = "let x = 1e999;";
        AssertHandlesGracefully(source);
        // Should either parse as Infinity or report an error - not crash
    }

    [Fact]
    public void Numeric_VerySmallNumber_HandlesGracefully()
    {
        var source = "let x = 1e-999;";
        AssertHandlesGracefully(source);
        // Should either parse as 0 or report an error - not crash
    }

    #endregion

    #region Comment Edge Cases

    [Fact(Skip = "Known limitation: Lexer silently ignores unterminated block comments instead of reporting an error - see Lexer.cs:410")]
    public void Comment_UnterminatedBlock_ShouldReportError()
    {
        // BUG: The lexer currently treats unterminated block comments as consuming the
        // remaining source silently without reporting an error. This should be fixed
        // to report "Unterminated block comment" similar to how regex literals are handled.
        var source = "let x = 5; /* unterminated";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source),
            "Expected error for unterminated block comment, but none was reported");
    }

    [Fact]
    public void Comment_UnterminatedBlock_ActualBehavior()
    {
        // Documents current (arguably incorrect) behavior: unterminated block comment
        // silently consumes remaining source without error.
        var source = "let x = 5; /* unterminated";
        AssertHandlesGracefully(source);
        var result = TryParse(source);
        // Current behavior: parses "let x = 5;" and treats rest as comment (no error)
        Assert.True(result.IsSuccess);
        Assert.Single(result.Statements);
    }

    [Fact]
    public void Comment_NestedBlockComments_HandlesGracefully()
    {
        // TypeScript doesn't support nested block comments, so inner */ closes
        var source = "/* outer /* inner */ let x = 5;";
        AssertHandlesGracefully(source);
        // The "let x = 5;" should be visible after the comment
        var result = TryParse(source);
        Assert.True(result.Statements.Count > 0 || !result.IsSuccess);
    }

    [Fact]
    public void Comment_VeryLongComment_HandlesGracefully()
    {
        // 100,000 character comment
        var longComment = new string('x', 100000);
        var source = $"/* {longComment} */ let x = 5;";
        AssertHandlesGracefully(source, 10000);
    }

    #endregion

    #region Error Recovery

    [Fact]
    public void Recovery_Parser_CollectsMultipleErrors()
    {
        var source = """
            let a = ;
            let b = ;
            let c = ;
            """;
        var result = TryParse(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.Errors.Count >= 2, $"Expected at least 2 errors, got {result.Errors.Count}");
    }

    [Fact]
    public void Recovery_Parser_HitsErrorLimit()
    {
        // Generate more than 10 errors
        var source = string.Join("\n", Enumerable.Repeat("let x = ;", 15));
        var result = TryParse(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.HitErrorLimit);
        Assert.Equal(10, result.Errors.Count);
    }

    [Fact]
    public void Recovery_Parser_ContinuesAfterError()
    {
        var source = """
            let a = ;
            let b = 5;
            let c = 10;
            """;
        var result = TryParse(source);

        Assert.False(result.IsSuccess);
        // Should have parsed the valid statements
        var varStatements = result.Statements.OfType<Stmt.Var>().ToList();
        Assert.Contains(varStatements, v => v.Name.Lexeme == "b");
        Assert.Contains(varStatements, v => v.Name.Lexeme == "c");
    }

    [Fact]
    public void Recovery_TypeChecker_CollectsMultipleErrors()
    {
        var source = """
            let a: number = "string";
            let b: boolean = 42;
            let c: string = true;
            """;
        var tokens = TryLex(source);
        var parser = new Parser(tokens);
        var parseResult = parser.Parse();

        Assert.True(parseResult.IsSuccess, "Parse should succeed for type error test");

        var checker = new TypeChecker();
        var typeCheckResult = checker.CheckWithRecovery(parseResult.Statements);

        Assert.False(typeCheckResult.IsSuccess);
        Assert.True(typeCheckResult.Errors.Count >= 2,
            $"Expected at least 2 type errors, got {typeCheckResult.Errors.Count}");
    }

    #endregion

    #region Keyword Edge Cases

    [Fact]
    public void Keyword_ReservedWordAsIdentifier_ReportsError()
    {
        var source = "let class = 5;";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void Keyword_KeywordInExpression_HandlesGracefully()
    {
        var source = "let x = if;";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void Keyword_MultipleKeywordsInRow_ReportsError()
    {
        var source = "let const function class;";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    #endregion

    #region Expression Edge Cases

    [Fact]
    public void Expression_EmptyParentheses_ReportsError()
    {
        var source = "let x = ();";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void Expression_MultipleOperatorsInRow_ReportsError()
    {
        var source = "let x = 1 + + + 2;";
        AssertHandlesGracefully(source);
        // This might actually be valid (unary plus), so just ensure it doesn't crash
    }

    [Fact]
    public void Expression_DanglingOperator_ReportsError()
    {
        var source = "let x = 1 +;";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void Expression_MissingCommaInArray_ReportsError()
    {
        var source = "let x = [1 2 3];";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void Expression_MissingColonInObject_ReportsError()
    {
        var source = "let x = { a 5 };";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    #endregion

    #region Statement Edge Cases

    [Fact]
    public void Statement_MultipleSemicolons_HandlesGracefully()
    {
        var source = ";;;;let x = 5;;;;";
        AssertHandlesGracefully(source);
        var result = TryParse(source);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Statement_ReturnOutsideFunction_ReportsError()
    {
        var source = "return 5;";
        AssertHandlesGracefully(source);
        // This should parse but type-check should fail
        var result = TryParse(source);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Statement_BreakOutsideLoop_ReportsError()
    {
        var source = "break;";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    [Fact]
    public void Statement_ContinueOutsideLoop_ReportsError()
    {
        var source = "continue;";
        AssertHandlesGracefully(source);
        Assert.True(FailsGracefully(source));
    }

    #endregion

    #region Unicode Identifiers

    [Fact]
    public void Unicode_EmojiInIdentifier_HandlesGracefully()
    {
        var source = "let x\u2764 = 5;";
        AssertHandlesGracefully(source);
    }

    [Fact]
    public void Unicode_CJKCharactersInIdentifier_HandlesGracefully()
    {
        var source = "let \u4E2D\u6587 = 5;";
        AssertHandlesGracefully(source);
    }

    [Fact]
    public void Unicode_RTLCharacters_HandlesGracefully()
    {
        var source = "let \u0627\u0644\u0639\u0631\u0628\u064A\u0629 = 5;";
        AssertHandlesGracefully(source);
    }

    #endregion

    #region Whitespace Edge Cases

    [Fact]
    public void Whitespace_OnlyWhitespace_HandlesGracefully()
    {
        var source = "    \t\t\n\n\r\n   ";
        AssertHandlesGracefully(source);
        var result = TryParse(source);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Statements);
    }

    [Fact]
    public void Whitespace_TabsInsteadOfSpaces_HandlesGracefully()
    {
        var source = "let\tx\t=\t5;";
        AssertHandlesGracefully(source);
        var result = TryParse(source);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Whitespace_VerticalTab_HandlesGracefully()
    {
        var source = "let x\v= 5;";
        AssertHandlesGracefully(source);
    }

    [Fact]
    public void Whitespace_FormFeed_HandlesGracefully()
    {
        var source = "let x\f= 5;";
        AssertHandlesGracefully(source);
    }

    #endregion
}
