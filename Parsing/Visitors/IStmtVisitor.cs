namespace SharpTS.Parsing.Visitors;

/// <summary>
/// Visitor interface for Stmt nodes with strongly-typed return values.
/// Implement all methods to ensure exhaustive handling of all statement types.
/// </summary>
/// <remarks>
/// This interface provides compile-time safety when adding new statement types.
/// Adding a new Stmt subtype requires adding a corresponding Visit method here,
/// which will cause compilation errors in all implementers until they handle it.
/// </remarks>
/// <typeparam name="TResult">The return type for all visit methods.</typeparam>
public interface IStmtVisitor<out TResult>
{
    TResult VisitExpression(Stmt.Expression stmt);
    TResult VisitVar(Stmt.Var stmt);
    TResult VisitConst(Stmt.Const stmt);
    TResult VisitFunction(Stmt.Function stmt);
    TResult VisitField(Stmt.Field stmt);
    TResult VisitAccessor(Stmt.Accessor stmt);
    TResult VisitAutoAccessor(Stmt.AutoAccessor stmt);
    TResult VisitClass(Stmt.Class stmt);
    TResult VisitStaticBlock(Stmt.StaticBlock stmt);
    TResult VisitInterface(Stmt.Interface stmt);
    TResult VisitBlock(Stmt.Block stmt);
    TResult VisitSequence(Stmt.Sequence stmt);
    TResult VisitReturn(Stmt.Return stmt);
    TResult VisitWhile(Stmt.While stmt);
    TResult VisitFor(Stmt.For stmt);
    TResult VisitDoWhile(Stmt.DoWhile stmt);
    TResult VisitForOf(Stmt.ForOf stmt);
    TResult VisitForIn(Stmt.ForIn stmt);
    TResult VisitIf(Stmt.If stmt);
    TResult VisitPrint(Stmt.Print stmt);
    TResult VisitBreak(Stmt.Break stmt);
    TResult VisitContinue(Stmt.Continue stmt);
    TResult VisitLabeledStatement(Stmt.LabeledStatement stmt);
    TResult VisitSwitch(Stmt.Switch stmt);
    TResult VisitTryCatch(Stmt.TryCatch stmt);
    TResult VisitThrow(Stmt.Throw stmt);
    TResult VisitTypeAlias(Stmt.TypeAlias stmt);
    TResult VisitEnum(Stmt.Enum stmt);
    TResult VisitNamespace(Stmt.Namespace stmt);
    TResult VisitImportAlias(Stmt.ImportAlias stmt);
    TResult VisitImportRequire(Stmt.ImportRequire stmt);
    TResult VisitImport(Stmt.Import stmt);
    TResult VisitExport(Stmt.Export stmt);
    TResult VisitFileDirective(Stmt.FileDirective stmt);
    TResult VisitDirective(Stmt.Directive stmt);
    TResult VisitDeclareModule(Stmt.DeclareModule stmt);
    TResult VisitDeclareGlobal(Stmt.DeclareGlobal stmt);
    TResult VisitUsing(Stmt.Using stmt);
}
