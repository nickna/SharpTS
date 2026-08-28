using Xunit;
using Xunit.Abstractions;

namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Acceptance tests for #84: the runner classifies a hand-picked test into
/// one of the buckets and the result is actionable. We don't assert <c>Pass</c>
/// — SharpTS isn't 100% conformant and the point is to surface where it isn't.
/// The bar is that the pipeline runs end-to-end without throwing.
/// </summary>
public class TypeScriptConformanceRunnerTests
{
    private readonly ITestOutputHelper _output;

    public TypeScriptConformanceRunnerTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The canonical hand-picked test from #84's acceptance criteria.
    /// Exercises conditional types — dense type-system mechanics, no lib
    /// dependency. Whatever bucket it lands in is fine; we just need
    /// classification to succeed.
    /// </summary>
    [Fact]
    public void RunOne_ConditionalTypes1_ClassifiesIntoABucket()
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null)
        {
            _output.WriteLine("external/typescript not initialized — skipping");
            return;
        }

        var testPath = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            "types", "conditional", "conditionalTypes1.ts");
        Assert.True(File.Exists(testPath), $"Expected hand-picked test at {testPath}");

        var runner = new TypeScriptConformanceRunner(root);
        var result = runner.RunOne(testPath);

        _output.WriteLine($"outcome: {result.Outcome}");
        if (result.Message is not null) _output.WriteLine($"  message: {result.Message}");
        if (result.SkipReason is not null) _output.WriteLine($"  skip:    {result.SkipReason}");
        if (result.ExpectedDiagnostics is { Count: > 0 } expected)
            _output.WriteLine($"  expected ({expected.Count}): {string.Join(", ", expected.Take(5).Select(d => $"{d.TsCode}@L{d.Line}"))}{(expected.Count > 5 ? "..." : "")}");
        if (result.ActualDiagnostics is { Count: > 0 } actual)
            _output.WriteLine($"  actual   ({actual.Count}): {string.Join(", ", actual.Take(5).Select(d => $"{d.TsCode}@L{d.Line}"))}{(actual.Count > 5 ? "..." : "")}");

        Assert.NotEqual(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
    }

    [Fact]
    public void RunOne_Es2018IntlApis_MatchesTargetLibraryBaseline()
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            "es2018",
            "es2018IntlAPIs.ts");

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(
            result.Outcome == TypeScriptConformanceOutcome.Pass,
            result.Message ?? result.Outcome.ToString());
    }

    [Fact]
    public void RunOne_TsxElementResolution19_MatchesEmptyDiagnosticBaseline()
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            "jsx",
            "tsxElementResolution19.tsx");

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(
            result.Outcome == TypeScriptConformanceOutcome.Pass,
            result.Message ?? result.Outcome.ToString());
    }

    [Fact]
    public void RunOne_TsxReferenceToHarnessLib_ResolvesFixture()
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            "jsx",
            "checkJsxChildrenProperty1.tsx");

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.NotEqual(TypeScriptConformanceOutcome.ParseError, result.Outcome);
        Assert.NotEqual(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
    }

    [Theory]
    [InlineData("checkJsxNamespaceNamesQuestionableForms.tsx")]
    [InlineData("jsxAndTypeAssertion.tsx")]
    [InlineData("jsxCheckJsxNoTypeArgumentsAllowed.tsx")]
    [InlineData("jsxInvalidEsprimaTestSuite.tsx")]
    [InlineData("jsxParsingError1.tsx")]
    [InlineData("jsxParsingError2.tsx")]
    [InlineData("jsxParsingError3.tsx")]
    [InlineData("jsxParsingErrorImmediateSpreadInAttributeValue.tsx")]
    [InlineData("jsxUnclosedParserRecovery.ts")]
    [InlineData("tsxAttributeInvalidNames.tsx")]
    [InlineData("tsxErrorRecovery1.tsx")]
    [InlineData("tsxErrorRecovery2.tsx")]
    [InlineData("tsxErrorRecovery3.tsx")]
    [InlineData("tsxGenericArrowFunctionParsing.tsx")]
    [InlineData("tsxNamespacedAttributeName1.tsx")]
    [InlineData("tsxNamespacedTagName1.tsx")]
    [InlineData("tsxNamespacedTagName2.tsx")]
    [InlineData("tsxNoJsx.tsx")]
    [InlineData("tsxOpeningClosingNames.tsx")]
    [InlineData("tsxParseTests1.tsx")]
    [InlineData("tsxParseTests2.tsx")]
    [InlineData("unicodeEscapesInJsxtags.tsx")]
    public void RunOne_JsxSyntaxRecoveryCampaign_MatchesPinnedDiagnostics(string fileName)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(TypeScriptConformancePaths.ConformanceDir(root), "jsx", fileName);

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(result.Outcome == TypeScriptConformanceOutcome.Pass,
            $"{fileName}: {result.Message ?? result.Outcome.ToString()}");
    }

    [Theory]
    [InlineData("Symbols/ES5SymbolProperty3.ts")]
    [InlineData("Symbols/ES5SymbolProperty4.ts")]
    [InlineData("Symbols/ES5SymbolProperty5.ts")]
    [InlineData("Symbols/ES5SymbolProperty7.ts")]
    [InlineData("es6/Symbols/symbolDeclarationEmit12.ts")]
    [InlineData("es6/Symbols/symbolProperty11.ts")]
    [InlineData("es6/Symbols/symbolProperty21.ts")]
    [InlineData("es6/Symbols/symbolProperty24.ts")]
    [InlineData("es6/Symbols/symbolProperty28.ts")]
    [InlineData("es6/Symbols/symbolProperty40.ts")]
    [InlineData("es6/Symbols/symbolProperty41.ts")]
    [InlineData("es6/Symbols/symbolProperty46.ts")]
    [InlineData("es6/Symbols/symbolProperty47.ts")]
    [InlineData("es6/Symbols/symbolProperty55.ts")]
    [InlineData("es6/Symbols/symbolProperty58.ts")]
    [InlineData("es6/Symbols/symbolProperty59.ts")]
    [InlineData("es6/Symbols/symbolProperty61.ts")]
    [InlineData("es6/Symbols/symbolType1.ts")]
    [InlineData("es6/Symbols/symbolType11.ts")]
    [InlineData("es6/Symbols/symbolType15.ts")]
    [InlineData("es6/Symbols/symbolType19.ts")]
    [InlineData("es6/Symbols/symbolType3.ts")]
    [InlineData("es6/Symbols/symbolType9.ts")]
    public void RunOne_SymbolCampaign_MatchesPinnedDiagnostics(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(result.Outcome == TypeScriptConformanceOutcome.Pass,
            $"{relativePath}: {result.Message ?? result.Outcome.ToString()}");
    }

    [Theory]
    [InlineData("Symbols/ES5SymbolProperty1.ts")]
    [InlineData("jsx/tsxElementResolution8.tsx")]
    [InlineData("types/conditional/inferTypes1.ts")]
    [InlineData("types/typeRelationships/assignmentCompatibility/assignmentCompatWithCallSignatures.ts")]
    [InlineData("types/typeRelationships/assignmentCompatibility/assignmentCompatWithConstructSignatures.ts")]
    [InlineData("types/typeRelationships/subtypesAndSuperTypes/subtypesOfTypeParameterWithConstraints4.ts")]
    public void RunOne_SymbolChanges_PreserveExistingPasses(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(result.Outcome == TypeScriptConformanceOutcome.Pass,
            $"{relativePath}: {result.Message ?? result.Outcome.ToString()}");
    }

    [Theory]
    [InlineData("jsx/jsxParsingError4.tsx")]
    [InlineData("jsx/tsxDynamicTagName1.tsx")]
    [InlineData("jsx/tsxDynamicTagName6.tsx")]
    [InlineData("jsx/tsxDynamicTagName9.tsx")]
    [InlineData("jsx/tsxElementResolution17.tsx")]
    [InlineData("jsx/tsxSpreadAttributesResolution16.tsx")]
    [InlineData("types/typeRelationships/assignmentCompatibility/assignmentCompatWithObjectMembers.ts")]
    public void RunOne_JsxRecoveryChanges_PreserveExistingPasses(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(result.Outcome == TypeScriptConformanceOutcome.Pass,
            $"{relativePath}: {result.Message ?? result.Outcome.ToString()}");
    }

    [Theory]
    [InlineData("jsx/correctlyMarkAliasAsReferences2.tsx")]
    [InlineData("jsx/correctlyMarkAliasAsReferences4.tsx")]
    [InlineData("jsx/inline/inlineJsxAndJsxFragPragma.tsx")]
    [InlineData("jsx/inline/inlineJsxFactoryDeclarations.tsx")]
    [InlineData("jsx/inline/inlineJsxFactoryWithFragmentIsError.tsx")]
    [InlineData("jsx/jsxs/jsxJsxsCjsTransformChildren.tsx")]
    [InlineData("jsx/jsxs/jsxJsxsCjsTransformSubstitutesNames.tsx")]
    [InlineData("jsx/jsxs/jsxJsxsCjsTransformSubstitutesNamesFragment.tsx")]
    [InlineData("jsx/tsxEmit2.tsx")]
    [InlineData("jsx/tsxExternalModuleEmit1.tsx")]
    [InlineData("jsx/tsxPreserveEmit1.tsx")]
    [InlineData("jsx/tsxPreserveEmit2.tsx")]
    [InlineData("jsx/tsxReactEmit6.tsx")]
    [InlineData("jsx/tsxReactEmit7.tsx")]
    [InlineData("jsx/tsxReactEmit8.tsx")]
    public void RunOne_Issue1533ResolutionCohort_MatchesDiagnosticBaseline(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        string path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(result.Outcome == TypeScriptConformanceOutcome.Pass,
            $"{relativePath}: {result.Message ?? result.Outcome.ToString()}");
    }

    [Theory]
    [InlineData("jsx/checkJsxGenericTagHasCorrectInferences.tsx")]
    [InlineData("jsx/checkJsxIntersectionElementPropsType.tsx")]
    [InlineData("jsx/checkJsxSubtleSkipContextSensitiveBug.tsx")]
    [InlineData("jsx/checkJsxUnionSFXContextualTypeInferredCorrectly.tsx")]
    [InlineData("jsx/inline/inlineJsxFactoryDeclarationsLocalTypes.tsx")]
    [InlineData("jsx/inline/inlineJsxFactoryLocalTypeGlobalFallback.tsx")]
    [InlineData("jsx/tsxElementResolution9.tsx")]
    [InlineData("jsx/tsxGenericAttributesType1.tsx")]
    [InlineData("jsx/tsxGenericAttributesType3.tsx")]
    [InlineData("jsx/tsxGenericAttributesType4.tsx")]
    [InlineData("jsx/tsxGenericAttributesType5.tsx")]
    [InlineData("jsx/tsxGenericAttributesType6.tsx")]
    [InlineData("jsx/tsxGenericAttributesType7.tsx")]
    [InlineData("jsx/tsxGenericAttributesType8.tsx")]
    [InlineData("jsx/tsxGenericAttributesType9.tsx")]
    [InlineData("jsx/tsxReactComponentWithDefaultTypeParameter1.tsx")]
    [InlineData("jsx/tsxReactComponentWithDefaultTypeParameter2.tsx")]
    [InlineData("jsx/tsxReactComponentWithDefaultTypeParameter3.tsx")]
    [InlineData("jsx/tsxSfcReturnUndefinedStrictNullChecks.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponentOverload1.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponentOverload2.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponentOverload3.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponentOverload4.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponentOverload5.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponentOverload6.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponents1.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponents2.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponents3.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponentsWithTypeArguments2.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponentsWithTypeArguments3.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponentsWithTypeArguments4.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponentsWithTypeArguments5.tsx")]
    [InlineData("jsx/tsxStatelessFunctionComponentWithDefaultTypeParameter2.tsx")]
    [InlineData("jsx/tsxTypeArgumentResolution.tsx")]
    [InlineData("jsx/tsxUnionElementType1.tsx")]
    [InlineData("jsx/tsxUnionElementType2.tsx")]
    [InlineData("jsx/tsxUnionElementType3.tsx")]
    [InlineData("jsx/tsxUnionElementType4.tsx")]
    [InlineData("jsx/tsxUnionElementType5.tsx")]
    [InlineData("jsx/tsxUnionElementType6.tsx")]
    [InlineData("jsx/tsxUnionTypeComponent1.tsx")]
    [InlineData("jsx/tsxUnionTypeComponent2.tsx")]
    public void RunOne_Issue1535GenericJsxCampaign_MatchesPinnedDiagnostics(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        string path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(result.Outcome == TypeScriptConformanceOutcome.Pass,
            $"{relativePath}: {result.Message ?? result.Outcome.ToString()}; actual: " +
            string.Join(", ", (result.ActualDiagnostics ?? []).Select(d => $"{d.TsCode}@L{d.Line}")));
    }

    [Theory]
    [InlineData("jsx/checkJsxChildrenProperty15.tsx")]
    [InlineData("jsx/tsxGenericAttributesType2.tsx")]
    [InlineData("types/conditional/inferTypes1.ts")]
    [InlineData("types/typeRelationships/assignmentCompatibility/assignmentCompatWithObjectMembers.ts")]
    public void RunOne_Issue1535GenericJsxCampaign_PreservesExistingPasses(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        string path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(result.Outcome == TypeScriptConformanceOutcome.Pass,
            $"{relativePath}: {result.Message ?? result.Outcome.ToString()}");
    }

    [Theory]
    [InlineData("types/typeRelationships/assignmentCompatibility/assignmentCompatWithDiscriminatedUnion.ts")]
    [InlineData("types/typeRelationships/assignmentCompatibility/enumAssignability.ts")]
    [InlineData("types/typeRelationships/assignmentCompatibility/enumAssignabilityInInheritance.ts")]
    [InlineData("types/typeRelationships/assignmentCompatibility/typeParameterAssignability.ts")]
    [InlineData("types/typeRelationships/assignmentCompatibility/typeParameterAssignability2.ts")]
    [InlineData("types/typeRelationships/assignmentCompatibility/typeParameterAssignability3.ts")]
    [InlineData("types/typeRelationships/assignmentCompatibility/undefinedAssignableToEveryType.ts")]
    [InlineData("types/typeRelationships/subtypesAndSuperTypes/enumIsNotASubtypeOfAnythingButNumber.ts")]
    [InlineData("types/typeRelationships/subtypesAndSuperTypes/nullIsSubtypeOfEverythingButUndefined.ts")]
    [InlineData("types/typeRelationships/subtypesAndSuperTypes/subtypesOfTypeParameterWithConstraints.ts")]
    [InlineData("types/typeRelationships/subtypesAndSuperTypes/subtypesOfTypeParameterWithRecursiveConstraints.ts")]
    [InlineData("types/typeRelationships/subtypesAndSuperTypes/undefinedIsSubtypeOfEverything.ts")]
    [InlineData("types/typeRelationships/subtypesAndSuperTypes/unionSubtypeIfEveryConstituentTypeIsSubtype.ts")]
    public void RunOne_RelationshipCampaign_MatchesPinnedDiagnostics(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(result.Outcome == TypeScriptConformanceOutcome.Pass,
            $"{relativePath}: {result.Message ?? result.Outcome.ToString()}");
    }

    [Theory]
    [InlineData("es6/Symbols/symbolProperty1.ts")]
    [InlineData("jsx/tsxSpreadAttributesResolution13.tsx")]
    [InlineData("types/typeRelationships/assignmentCompatibility/assignmentCompatWithObjectMembersAccessibility.ts")]
    public void RunOne_RelationshipCampaign_PreservesExistingPasses(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(result.Outcome == TypeScriptConformanceOutcome.Pass,
            $"{relativePath}: {result.Message ?? result.Outcome.ToString()}");
    }

    [Theory]
    [InlineData("es2022/arbitraryModuleNamespaceIdentifiers/arbitraryModuleNamespaceIdentifiers_syntax.ts")]
    [InlineData("jsx/jsxParsingError2.tsx")]
    [InlineData("jsx/jsxAttributeInitializer.ts")]
    [InlineData("jsx/jsxInvalidEsprimaTestSuite.tsx")]
    [InlineData("jsx/tsxElementResolution17.tsx")]
    [InlineData("jsx/tsxNamespacedTagName1.tsx")]
    [InlineData("jsx/tsxReactEmitEntities.tsx")]
    public void RunOne_IntentionalSyntaxErrorsBecomeComparableDiagnostics(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.NotEqual(TypeScriptConformanceOutcome.ParseError, result.Outcome);
        Assert.NotEqual(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
        Assert.NotEqual(TypeScriptConformanceOutcome.TypeCheckError, result.Outcome);
    }

    [Theory]
    [InlineData("es2019/importMeta/importMeta.ts")]
    [InlineData("es2020/modules/exportAsNamespace_nonExistent.ts")]
    [InlineData("es2022/es2024SharedMemory.ts")]
    [InlineData("jsx/inline/inlineJsxFactoryDeclarations.tsx")]
    [InlineData("jsx/inline/inlineJsxFactoryDeclarationsLocalTypes.tsx")]
    [InlineData("jsx/inline/inlineJsxFactoryLocalTypeGlobalFallback.tsx")]
    public void RunOne_ExpandedSubset_DoesNotCrashParserOrChecker(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);
        _output.WriteLine(result.Message ?? result.Outcome.ToString());

        Assert.NotEqual(TypeScriptConformanceOutcome.ParseError, result.Outcome);
        Assert.NotEqual(TypeScriptConformanceOutcome.TypeCheckError, result.Outcome);
    }

    [Theory]
    [InlineData("es2019/globalThisAmbientModules.ts")]
    [InlineData("es2019/globalThisBlockscopedProperties.ts")]
    [InlineData("es2019/globalThisCollision.ts")]
    [InlineData("es2019/globalThisPropertyAssignment.ts")]
    [InlineData("es2019/globalThisUnknown.ts")]
    [InlineData("es2019/globalThisVarDeclaration.ts")]
    [InlineData("decorators/class/decoratorOnClass1.ts")]
    [InlineData("jsx/jsxCheckJsxNoTypeArgumentsAllowed.tsx")]
    [InlineData("types/conditional/inferTypesInvalidExtendsDeclaration.ts")]
    public void RunOne_ReducedSkipSurface_IsMeasured(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        var projectDir = TypeScriptConformancePaths.TryFindProjectDir();
        if (root is null || projectDir is null) return;

        string configDir = Path.Combine(projectDir, "config");
        var config = TypeScriptConformanceConfig.Load(Path.Combine(configDir, "subset.json"));
        var runner = new TypeScriptConformanceRunner(
            root,
            config.LoadSkipDirectives(configDir),
            config.LoadSkipTests(configDir));
        var path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = runner.RunOne(path);

        Assert.True(
            result.Outcome is TypeScriptConformanceOutcome.Pass or TypeScriptConformanceOutcome.Fail,
            $"Expected a measured diagnostic outcome, got {result.Outcome}: " +
            (result.Message ?? result.SkipReason));
    }

    [Fact]
    public void RunOne_NonexistentFile_ReturnsHarnessError()
    {
        var runner = new TypeScriptConformanceRunner("/nonexistent");
        var result = runner.RunOne("/nonexistent/missing.ts");
        Assert.Equal(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
        Assert.Contains("Failed to read", result.Message);
    }

    [Theory]
    [InlineData("es2016/es2016IntlAPIs.ts")]
    [InlineData("es2017/useObjectValuesAndEntries2.ts")]
    [InlineData("es2017/useObjectValuesAndEntries3.ts")]
    [InlineData("es2017/useSharedArrayBuffer2.ts")]
    [InlineData("es2017/useSharedArrayBuffer3.ts")]
    [InlineData("es2017/useSharedArrayBuffer6.ts")]
    [InlineData("es2018/useRegexpGroups.ts")]
    [InlineData("es2019/globalThisAmbientModules.ts")]
    [InlineData("es2019/globalThisBlockscopedProperties.ts")]
    [InlineData("es2019/globalThisCollision.ts")]
    [InlineData("es2019/globalThisGlobalExportAsGlobal.ts")]
    [InlineData("es2019/globalThisPropertyAssignment.ts")]
    [InlineData("es2019/globalThisReadonlyProperties.ts")]
    [InlineData("es2019/globalThisUnknown.ts")]
    [InlineData("es2019/globalThisUnknownNoImplicitAny.ts")]
    [InlineData("es2019/globalThisVarDeclaration.ts")]
    [InlineData("es2019/importMeta/importMeta.ts")]
    [InlineData("es2019/importMeta/importMetaNarrowing.ts")]
    [InlineData("es2020/es2020IntlAPIs.ts")]
    [InlineData("es2020/intlNumberFormatES2020.ts")]
    [InlineData("es2020/localesObjectArgument.ts")]
    [InlineData("es2020/modules/exportAsNamespace1.ts")]
    [InlineData("es2020/modules/exportAsNamespace2.ts")]
    [InlineData("es2020/modules/exportAsNamespace3.ts")]
    [InlineData("es2020/modules/exportAsNamespace_exportAssignment.ts")]
    [InlineData("es2020/modules/exportAsNamespace_missingEmitHelpers.ts")]
    [InlineData("es2021/logicalAssignment/logicalAssignment4.ts")]
    [InlineData("es2021/logicalAssignment/logicalAssignment6.ts")]
    [InlineData("es2021/logicalAssignment/logicalAssignment7.ts")]
    [InlineData("es2021/logicalAssignment/logicalAssignment8.ts")]
    [InlineData("es2022/arbitraryModuleNamespaceIdentifiers/arbitraryModuleNamespaceIdentifiers_importEmpty.ts")]
    [InlineData("es2022/arbitraryModuleNamespaceIdentifiers/arbitraryModuleNamespaceIdentifiers_module.ts")]
    [InlineData("es2022/arbitraryModuleNamespaceIdentifiers/arbitraryModuleNamespaceIdentifiers_syntax.ts")]
    [InlineData("es2022/es2024SharedMemory.ts")]
    [InlineData("es2023/intlNumberFormatES5UseGrouping.ts")]
    [InlineData("es2023/intlNumberFormatES2023.ts")]
    [InlineData("esnext/esnextSharedMemory.ts")]
    [InlineData("jsx/tsxEmit3.tsx")]
    public void RunOne_Issue1540Candidate_MatchesPinnedTypeScript(string relativePath)
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;
        string path = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var result = new TypeScriptConformanceRunner(root).RunOne(path);

        Assert.True(result.Outcome == TypeScriptConformanceOutcome.Pass,
            $"{relativePath}: {result.Outcome}: {result.Message}");
    }

    [Fact]
    public void ResolveBaselinePath_SelectsFinalModuleAndTargetVariant()
    {
        string root = Path.Combine(Path.GetTempPath(), $"sharpts-ts-{Guid.NewGuid():N}");
        string baselines = Path.Combine(root, "tests", "baselines", "reference");
        Directory.CreateDirectory(baselines);
        try
        {
            string expected = Path.Combine(baselines, "sample(module=es2020,target=es5).errors.txt");
            File.WriteAllText(expected, "");
            File.WriteAllText(Path.Combine(baselines, "sample(module=esnext,target=esnext).errors.txt"), "");
            var metadata = TypeScriptConformanceMetadataParser.Parse(
                "sample.ts",
                "// @target: esnext, es5\n// @module: esnext, commonjs, es2020\n");

            string actual = new TypeScriptConformanceRunner(root)
                .ResolveBaselinePath("sample.ts", metadata);

            Assert.Equal(expected, actual);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ResolveBaselinePath_UsesEsNextForWildcardModuleVariant()
    {
        string root = Path.Combine(Path.GetTempPath(), $"sharpts-ts-{Guid.NewGuid():N}");
        string baselines = Path.Combine(root, "tests", "baselines", "reference");
        Directory.CreateDirectory(baselines);
        try
        {
            string expected = Path.Combine(baselines, "sample(module=esnext).errors.txt");
            File.WriteAllText(expected, "");
            File.WriteAllText(Path.Combine(baselines, "sample(module=commonjs).errors.txt"), "");
            var metadata = TypeScriptConformanceMetadataParser.Parse(
                "sample.ts",
                "// @module: *\n");

            string actual = new TypeScriptConformanceRunner(root)
                .ResolveBaselinePath("sample.ts", metadata);

            Assert.Equal(expected, actual);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RunOne_DirectiveSkip_ShortCircuits()
    {
        // Build a tiny test with @experimentalDecorators set, configure the
        // runner to skip it. Verifies the skip-list short-circuit before any
        // parse/check work happens.
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "// @experimentalDecorators: true\nconst x = 1;\n");
            var runner = new TypeScriptConformanceRunner(
                "/fake-root",
                skipDirectives: new HashSet<string>(StringComparer.Ordinal) { "experimentaldecorators" });
            var result = runner.RunOne(tmp);
            Assert.Equal(TypeScriptConformanceOutcome.Skipped, result.Outcome);
            Assert.Equal("directive:experimentaldecorators", result.SkipReason);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RunOne_MultiFile_UsesProgramResolver()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp,
                "// @filename: a.ts\nexport const x = 1;\n// @filename: b.ts\nimport { x } from './a';\n");
            var runner = new TypeScriptConformanceRunner("/fake-root");
            var result = runner.RunOne(tmp);
            Assert.NotEqual(TypeScriptConformanceOutcome.Skipped, result.Outcome);
            Assert.NotEqual(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
            Assert.NotEqual(TypeScriptConformanceOutcome.ParseError, result.Outcome);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RunOne_MultiFile_ReportsDiagnosticsFromEveryRoot()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp,
                "// @filename: a.ts\nexport const x: string = 1;\n" +
                "// @filename: b.ts\nimport { x } from './a';\nconst y: number = x;\n");
            var result = new TypeScriptConformanceRunner("/fake-root").RunOne(tmp);

            Assert.NotEqual(TypeScriptConformanceOutcome.TypeCheckError, result.Outcome);
            Assert.Contains(result.ActualDiagnostics ?? [], d => d.TsCode == "TS2322");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RunOne_ModuleRecovery_ReportsEveryErrorInsideFunctionBody()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "function f<T, U>(t: T, u: U) {\n  t = u;\n  u = t;\n}\n");
            var result = new TypeScriptConformanceRunner("/fake-root").RunOne(tmp);

            var errors = result.ActualDiagnostics ?? [];
            Assert.Equal(2, errors.Count(d => d.TsCode == "TS2322"));
            Assert.Contains(errors, d => d.TsCode == "TS2322" && d.Line == 2);
            Assert.Contains(errors, d => d.TsCode == "TS2322" && d.Line == 3);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RunOne_ObjectTargetsRejectNullableUnion()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "// @strictNullChecks: true\ndeclare let value: string | null;\nlet upper: Object = value;\nlet lower: object = value;\n");
            var result = new TypeScriptConformanceRunner("/fake-root").RunOne(tmp);

            var errors = result.ActualDiagnostics ?? [];
            Assert.Equal(2, errors.Count(d => d.TsCode == "TS2322"));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RunOne_GenericAmbientClassRecovery_ReportsEveryInvalidMember()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "declare class C<T> {\n  first: unique symbol;\n  second(): unique symbol;\n}\n");
            var result = new TypeScriptConformanceRunner("/fake-root").RunOne(tmp);

            var errors = result.ActualDiagnostics ?? [];
            Assert.Equal(2, errors.Count(d => d.TsCode == "TS1331"));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RunOne_TsxWithGlobalIntrinsicElements_UsesProgramResolver()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"sharpts-{Guid.NewGuid():N}.tsx");
        try
        {
            // Automatic runtime: classic `@jsx: react` without a React import would be a
            // legitimate TS2304 under the JSX checking pipeline (as in tsc).
            File.WriteAllText(tmp, """
                // @jsx: react-jsx
                // @filename: renderer.d.ts
                declare global {
                    namespace JSX {
                        interface IntrinsicElements { button: { disabled?: boolean }; }
                    }
                }
                export {};
                // @filename: view.tsx
                <button disabled={false}></button>;
                """);

            var result = new TypeScriptConformanceRunner("/fake-root").RunOne(tmp);

            Assert.NotEqual(TypeScriptConformanceOutcome.ParseError, result.Outcome);
            Assert.NotEqual(TypeScriptConformanceOutcome.TypeCheckError, result.Outcome);
            Assert.DoesNotContain(
                result.ActualDiagnostics ?? [],
                diagnostic => diagnostic.TsCode is "TS2304" or "TS2339" or "TS1360");
        }
        finally { File.Delete(tmp); }
    }

    #region Strictness directives reach the checker

    /// <summary>
    /// Runs a synthetic test through the real runner and returns the TS codes it produced.
    /// A <c>/fake-root</c> means no <c>*.errors.txt</c> baseline is found, so the outcome is
    /// uninteresting — what is pinned here is the directive → TypeChecker option wiring.
    /// </summary>
    private static IReadOnlyList<BaselineDiagnostic> ActualDiagnostics(string source)
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, source);
            var result = new TypeScriptConformanceRunner("/fake-root").RunOne(tmp);
            Assert.NotEqual(TypeScriptConformanceOutcome.HarnessError, result.Outcome);
            Assert.NotEqual(TypeScriptConformanceOutcome.TypeCheckError, result.Outcome);
            return result.ActualDiagnostics ?? [];
        }
        finally { File.Delete(tmp); }
    }

    private static IReadOnlyList<string> ActualCodes(string source) =>
        ActualDiagnostics(source).Select(d => d.TsCode).ToList();

    // An unannotated parameter on a DECLARED function — the one shape SharpTS reports
    // noImplicitAny for. Arrows are deliberately exempt, so they cannot be used here.
    private const string ImplicitAnyParam = "function f(x) { return x; }\n";

    [Fact]
    public void MultipleJsxModes_SelectFinalHarnessVariant()
    {
        var codes = ActualCodes("""
            // @strict: false
            // @jsx: react, react-jsx
            // @filename: view.tsx
            declare namespace JSX { interface IntrinsicElements { div: {}; } }
            <div />;
            """);

        Assert.DoesNotContain("TS17004", codes);
    }

    [Fact]
    public void PreserveJsx_DoesNotRequireClassicFactory()
    {
        var diagnostics = ActualDiagnostics("""
            // @strict: false
            // @jsx: preserve
            // @filename: view.tsx
            declare namespace JSX { interface IntrinsicElements { div: {}; } }
            <div />;
            """);

        Assert.DoesNotContain(diagnostics, d => d.TsCode == "TS2304");
        Assert.DoesNotContain(diagnostics, d => d.TsCode == "TS17004");
    }

    [Fact]
    public void NoDirectives_UseTypeScript6StrictDefault()
    {
        Assert.Contains("TS7006", ActualCodes(ImplicitAnyParam));
    }

    [Fact]
    public void StrictDirective_EnablesNoImplicitAny()
    {
        Assert.Contains("TS7006", ActualCodes("// @strict: true\n" + ImplicitAnyParam));
    }

    [Fact]
    public void NoImplicitAnyDirective_EnablesItWithoutStrict()
    {
        Assert.Contains("TS7006", ActualCodes("// @noImplicitAny: true\n" + ImplicitAnyParam));
    }

    [Fact]
    public void NoImplicitAnyDirective_OverridesStrict()
    {
        // The specific directive beats the umbrella, matching how @strictNullChecks behaves.
        Assert.DoesNotContain("TS7006",
            ActualCodes("// @strict: true\n// @noImplicitAny: false\n" + ImplicitAnyParam));
    }

    [Fact]
    public void StrictDirectiveFalse_LeavesNoImplicitAnyOff()
    {
        Assert.DoesNotContain("TS7006", ActualCodes("// @strict: false\n" + ImplicitAnyParam));
    }

    #endregion
}

/// <summary>
/// Tests for the committed-baseline read/write/diff harness. Mirrors the
/// shape of <c>SharpTS.Test262.Test262BaselineDiffer</c>'s tests.
/// </summary>
public class TypeScriptConformanceBaselineTests
{
    [Fact]
    public void Header_IsVersionedAndPinsCorpus()
    {
        const string revision = "0123456789abcdef0123456789abcdef01234567";
        Assert.StartsWith(
            "# SharpTS baseline-format=1 suite=TypeScript corpus=" + revision + " — ",
            TypeScriptConformanceBaseline.Header(revision));
    }

    [Fact]
    public void EncodeBucket_PassWithNoSkipReason_JustOutcomeName()
    {
        var r = new TypeScriptConformanceResult(TypeScriptConformanceOutcome.Pass, null, null);
        Assert.Equal("Pass", TypeScriptConformanceBaseline.EncodeBucket(r));
    }

    [Fact]
    public void EncodeBucket_SkippedWithReason_AppendsReason()
    {
        var r = new TypeScriptConformanceResult(
            TypeScriptConformanceOutcome.Skipped,
            null,
            "directive:experimentaldecorators");
        Assert.Equal(
            "Skipped:directive:experimentaldecorators",
            TypeScriptConformanceBaseline.EncodeBucket(r));
    }

    [Fact]
    public void Diff_EmptyToEmpty_NoChanges()
    {
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        Assert.False(diff.HasHardFailures);
        Assert.Empty(diff.NewRegressions);
        Assert.Empty(diff.NewEntries);
    }

    [Fact]
    public void Diff_PassToFail_IsRegression()
    {
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string> { ["a.ts"] = "Pass" },
            new Dictionary<string, string> { ["a.ts"] = "Fail" });
        Assert.True(diff.HasHardFailures);
        Assert.Single(diff.NewRegressions);
        Assert.Equal("a.ts", diff.NewRegressions[0].RelPath);
    }

    [Fact]
    public void Diff_FailToPass_IsNewPassHardFailure()
    {
        // Forces baseline updates through review so improvements are noticed.
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string> { ["a.ts"] = "Fail" },
            new Dictionary<string, string> { ["a.ts"] = "Pass" });
        Assert.True(diff.HasHardFailures);
        Assert.Single(diff.NewPasses);
    }

    [Fact]
    public void Diff_SkippedReasonChange_IsBucketChangeNotHardFailure()
    {
        // Both Skipped — same "good" bucket, just different reason. Worth
        // surfacing but not worth failing the build.
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string> { ["a.ts"] = "Skipped:directive:foo" },
            new Dictionary<string, string> { ["a.ts"] = "Skipped:directive:bar" });
        Assert.False(diff.HasHardFailures);
        Assert.Single(diff.BucketChanges);
    }

    [Fact]
    public void Diff_RemovedEntry_IsTracked()
    {
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string> { ["a.ts"] = "Pass", ["b.ts"] = "Fail" },
            new Dictionary<string, string> { ["a.ts"] = "Pass" });
        Assert.Single(diff.RemovedEntries);
        Assert.Equal("b.ts", diff.RemovedEntries[0].RelPath);
    }
}
