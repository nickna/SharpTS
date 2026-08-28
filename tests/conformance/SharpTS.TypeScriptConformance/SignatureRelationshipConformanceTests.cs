using Xunit;

namespace SharpTS.TypeScriptConformance;

/// <summary>
/// The call/construct signature diagnostic tranche owned by #1536. Keeping the candidates in a
/// theory makes an individual semantic failure directly runnable while the frozen 534-file
/// baseline continues to guard the aggregate bucket distribution.
/// </summary>
[Collection("TypeScriptConformanceBaseline")]
public class SignatureRelationshipConformanceTests
{
    public static TheoryData<string> Cases => new()
    {
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithCallSignatures3.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithCallSignatures4.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithCallSignatures6.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithCallSignaturesWithOptionalParameters.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithCallSignaturesWithRestParameters.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithConstructSignatures3.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithConstructSignatures4.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithConstructSignaturesWithOptionalParameters.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithGenericCallSignatures.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithGenericCallSignatures2.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithGenericCallSignaturesWithOptionalParameters.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/callSignatureAssignabilityInInheritance.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/callSignatureAssignabilityInInheritance2.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/callSignatureAssignabilityInInheritance3.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/callSignatureAssignabilityInInheritance4.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/callSignatureAssignabilityInInheritance5.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/constructSignatureAssignabilityInInheritance.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/constructSignatureAssignabilityInInheritance2.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/constructSignatureAssignabilityInInheritance3.ts",
        "tests/cases/conformance/types/typeRelationships/assignmentCompatibility/constructSignatureAssignabilityInInheritance5.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithCallSignatures2.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithCallSignatures3.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithCallSignaturesA.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithCallSignaturesWithOptionalParameters.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithCallSignaturesWithRestParameters.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithCallSignaturesWithSpecializedSignatures.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithConstructSignatures.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithConstructSignatures3.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithConstructSignatures5.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithConstructSignaturesWithOptionalParameters.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithConstructSignaturesWithSpecializedSignatures.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithGenericCallSignaturesWithOptionalParameters.ts",
        "tests/cases/conformance/types/typeRelationships/subtypesAndSuperTypes/subtypingWithGenericConstructSignaturesWithOptionalParameters.ts",
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesTypeScriptDiagnostics(string relativePath)
    {
        string? root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null)
            return;

        var result = new TypeScriptConformanceRunner(root).RunOne(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.True(
            result.Outcome == TypeScriptConformanceOutcome.Pass,
            $"{relativePath}: {result.Message}");
    }

}
