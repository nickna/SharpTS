using System.Text;
using System.Text.RegularExpressions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.LanguageServer.Services;

internal sealed record NavigationHoverResult(Hover? Hover, AnalysisValidation? Validation = null)
{
    public bool IsCurrent(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Validation?.IsCurrent(cancellationToken) ?? true;
    }
}

/// <summary>Projects exact written source names from one completed shared analysis.</summary>
public sealed class SemanticHoverService(SemanticAnalysisService analysis, MemberHoverService? members = null)
{
    private readonly MemberHoverService _members = members ?? new();

    internal async Task<NavigationHoverResult> HoverAsync(DocumentRequestSnapshot capture,
        Position position, MarkupKind format, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (capture.Document.FilePath is null) return new(null);
        using AnalysisLease? lease = await analysis.GetDocumentAsync(capture, cancellationToken).ConfigureAwait(false);
        if (lease is null) return new(null);
        using var metadata = lease.EnterMetadataScope();
        CheckedNavigationModel model = lease.Model;
        int offset = model.Document.Lines.ToOffset(position.Line + 1, position.Character + 1);
        // ToOffset clamps invalid positions. Do not turn a different source position into a hover.
        var (line, column) = model.Document.Lines.ToPosition(offset);
        if (line - 1 != position.Line || column - 1 != position.Character)
            return new(null, lease.Validation);

        Hover? result = _members.Hover(model, offset, cancellationToken);
        if (result is null && Select(model, offset, cancellationToken) is { } selected)
            result = Render(model.Document, selected.Name, selected.Span, selected.Type, format);
        return new(AdaptMarkup(result, format), lease.Validation);
    }

    private sealed record Selection(string Name, SourceSpan Span, EditorTypePresentation Type);

    private static Selection? Select(CheckedNavigationModel model, int offset, CancellationToken cancellationToken)
    {
        if (model.Document.EditorSyntax is not { } syntax) return null;
        EditorSyntaxRecord? name = syntax.FindNarrowest(offset, EditorSyntaxKind.Name);
        if (name?.Token is not { } token || !token.Span.Contains(offset)) return null;
        cancellationToken.ThrowIfCancellationRequested();

        if (name.Node is TypeNode typeNode)
        {
            var use = model.EditorFacts.GetTypeUse(model.Document, typeNode);
            if (!Available(use?.Type, use?.Availability)) return null;
            if (typeNode is NamedTypeNode { NameTokens.Count: > 1 } qualified && qualified.NameTokens[^1].Span != token.Span)
                return BoundDeclaration(model, offset, token, name.Node, cancellationToken);
            return new(token.Lexeme, token.Span, use!.Type);
        }

        var declarations = model.EditorFacts.FindDeclarations(model.Document, offset);
        if (declarations.Count != 0)
        {
            var declaration = declarations.OrderBy(fact => fact.Facet).FirstOrDefault(fact =>
                Available(fact.Type, fact.Availability));
            return declaration is null ? null : new(declaration.LocalName, token.Span, declaration.Type);
        }

        if (syntax.FindMember(offset) is { } member && member.Name.Span.Contains(offset))
        {
            if (member.IsRecovered || member.IsIndex) return null;
            var read = model.EditorFacts.GetOccurrence(model.Document, member.Owner);
            if (model.Bindings.FindSymbols(model.Document, offset).Count != 0)
                return Available(read?.Type, read?.Availability)
                    ? BoundDeclaration(model, offset, token, name.Node, cancellationToken) : null;
            var resolution = model.Members.FindResolution(model.Document, offset);
            if (!resolution.IsResolved) return null;
            var symbol = resolution.Candidates[0];
            if (!SourceMemberAvailable(model, symbol, cancellationToken)) return null;
            // Read nodes return the selected member's type. Write/private-call nodes instead
            // return the RHS/call result and must use their proven receiver projection below.
            if ((member.Owner is Expr.Get or Expr.GetPrivate or Expr.Super) && Available(read?.Type, read?.Availability))
                return new(member.Name.Lexeme, member.Name.Span, read!.Type);
            var receiver = model.EditorFacts.GetOccurrence(model.Document, member.Receiver);
            if (member.Receiver is not Expr.Super && !Available(receiver?.Type, receiver?.Availability)) return null;
            var candidate = model.EditorFacts.GetReceiverMembers(model.Document, member.Receiver).Members
                .FirstOrDefault(candidate => candidate.Name == symbol.Name && candidate.Facet == symbol.Facet &&
                    candidate.Source is { } source && source.Id == symbol.Id && source.Generation == symbol.Generation);
            return candidate?.Type.IsAvailable == true ? new(member.Name.Lexeme, member.Name.Span, candidate.Type) : null;
        }

        // A private brand check owns a boolean result, not the private field's type.
        if (name.Node is Expr.PrivateIn)
        {
            var resolution = model.Members.FindResolution(model.Document, offset);
            return resolution.IsResolved ? MemberDeclaration(model, resolution.Candidates[0], token, cancellationToken) : null;
        }

        if (name.Node is Expr.Super super && token.Type == TokenType.SUPER)
        {
            var type = model.EditorFacts.GetReceiverMembers(model.Document, super).ReceiverType;
            return type?.IsAvailable == true ? new(token.Lexeme, token.Span, type) : null;
        }

        if (name.Node is not Expr expression)
            return name.Node is Stmt.ImportSpecifier or Stmt.Import
                ? BoundDeclaration(model, offset, token, name.Node, cancellationToken, requireOwnerFact: true) : null;
        if (name.Role != EditorSyntaxRole.Name) return null;
        var occurrence = model.EditorFacts.GetOccurrence(model.Document, expression);
        if (!Available(occurrence?.Type, occurrence?.Availability)) return null;
        if (expression is Expr.Assign or Expr.CompoundAssign or Expr.LogicalAssign)
        {
            // Assignment expressions return their RHS. The exact bound variable retains its
            // declared type; neither an RHS result nor a same-spelled visible local substitutes it.
            return BoundDeclaration(model, offset, token, name.Node, cancellationToken);
        }
        return expression is Expr.Variable or Expr.This
            ? new(token.Lexeme, token.Span, occurrence!.Type) : null;
    }

    private static bool SourceMemberAvailable(CheckedNavigationModel model, FrozenSourceMemberSymbol symbol,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in symbol.Declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var facts = model.EditorFacts.FindDeclarations(declaration.Document, declaration.Name.Start)
                .Where(fact => ReferenceEquals(fact.Source.Owner, declaration.Owner) && fact.Source.Name?.Span == declaration.Name.Span)
                .ToArray();
            if (facts.Length == 0 || facts.Any(fact => !Available(fact.Type, fact.Availability))) return false;
        }
        return symbol.Declarations.Count != 0;
    }

    private static Selection? BoundDeclaration(CheckedNavigationModel model, int offset, Token token, object owner,
        CancellationToken cancellationToken, bool requireOwnerFact = false)
    {
        var symbols = model.Bindings.FindSymbols(model.Document, offset);
        if (symbols.Count == 0) return null;
        BindingNamespace facet = symbols.Min(symbol => symbol.Namespace);
        var ownerTypes = model.EditorFacts.GetDeclarations(model.Document, owner).Where(fact =>
            fact.Facet == facet && fact.Binding is { } binding && symbols.Any(symbol => symbol.Id == binding.Id && symbol.Namespace == facet))
            .ToArray();
        if (ownerTypes.Length != 0)
            return ownerTypes.All(fact => Available(fact.Type, fact.Availability)) && Unanimous(ownerTypes.Select(fact => fact.Type)) is { } ownType
                ? new(token.Lexeme, token.Span, ownType) : null;
        if (requireOwnerFact) return null;
        var types = new List<EditorTypePresentation>();
        foreach (var symbol in symbols.Where(symbol => symbol.Namespace == facet))
            foreach (var declaration in symbol.Declarations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var facts = model.EditorFacts.FindDeclarations(declaration.Document, declaration.Name.Start)
                    .Where(fact => fact.Binding is { } binding && binding.Id == symbol.Id && binding.Facet == symbol.Namespace);
                foreach (var fact in facts)
                {
                    if (!Available(fact.Type, fact.Availability)) return null;
                    types.Add(fact.Type);
                }
            }
        return Unanimous(types) is { } declared ? new(token.Lexeme, token.Span, declared) : null;
    }

    private static Selection? MemberDeclaration(CheckedNavigationModel model, FrozenSourceMemberSymbol symbol,
        Token token, CancellationToken cancellationToken)
    {
        var types = new List<EditorTypePresentation>();
        foreach (var declaration in symbol.Declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var fact in model.EditorFacts.FindDeclarations(declaration.Document, declaration.Name.Start))
                if (ReferenceEquals(fact.Source.Owner, declaration.Owner) && Available(fact.Type, fact.Availability))
                    types.Add(fact.Type);
        }
        return Unanimous(types) is { } type ? new(token.Lexeme, token.Span, type) : null;
    }

    private static EditorTypePresentation? Unanimous(IEnumerable<EditorTypePresentation> types)
    {
        var values = types.Distinct().Take(2).ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static bool Available(EditorTypePresentation? type, EditorFactAvailability? availability) =>
        availability == EditorFactAvailability.Available && type?.IsAvailable == true;

    private static Hover Render(SourceDocument document, string name, SourceSpan span,
        EditorTypePresentation type, MarkupKind format)
    {
        const int limit = 4096;
        if (name.Length > 256) name = name[..SafeEnd(name, 256)] + "…";
        string label = name + ": " + type.Text;
        if (label.Length > limit) label = label[..SafeEnd(label, limit - 1)] + "…";
        string value = label;
        if (format == MarkupKind.Markdown)
        {
            int run = 0, longest = 0;
            foreach (char character in label)
            {
                run = character == '`' ? run + 1 : 0;
                longest = Math.Max(longest, run);
            }
            string fence = new('`', Math.Max(3, longest + 1));
            value = fence + "typescript\n" + label + "\n" + fence;
        }
        var (startLine, startColumn) = document.Lines.ToPosition(span.Start);
        var (endLine, endColumn) = document.Lines.ToPosition(span.End);
        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent { Kind = format, Value = value }),
            Range = new Range(startLine - 1, startColumn - 1, endLine - 1, endColumn - 1),
        };
    }

    private static int SafeEnd(string text, int end) => end > 0 && char.IsHighSurrogate(text[end - 1]) ? end - 1 : end;

    private static readonly Regex MarkdownLinks = new(@"\[([^\]\r\n]+)\]\(([^)\r\n]+)\)", RegexOptions.NonBacktracking);
    private static readonly Regex MarkdownEmphasis = new(@"(^|\s)_([^_\r\n]+)_($|\s)", RegexOptions.NonBacktracking);

    /// <summary>Preserves legacy hover text while respecting a plaintext-only client.</summary>
    internal static Hover? AdaptMarkup(Hover? hover, MarkupKind format)
    {
        if (hover?.Contents.MarkupContent is not { Kind: var kind } content || kind == format || format != MarkupKind.PlainText)
            return hover;
        var text = new StringBuilder();
        foreach (string line in content.Value.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)) continue;
            if (text.Length != 0) text.Append('\n');
            text.Append(line);
        }
        string value = MarkdownLinks.Replace(text.ToString(), "$1 ($2)").Replace("**", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);
        value = MarkdownEmphasis.Replace(value, "$1$2$3");
        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.PlainText, Value = value }),
            Range = hover.Range,
        };
    }
}
