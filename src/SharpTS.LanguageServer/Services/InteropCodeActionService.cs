using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SharpTS.LanguageServer.Services;

/// <summary>Builds safe interop quick fixes from structured diagnostic data.</summary>
public sealed class InteropCodeActionService
{
    public CommandOrCodeActionContainer Create(CodeActionParams request)
    {
        if (request.Context.Only is { } requestedKinds &&
            !requestedKinds.Contains(CodeActionKind.QuickFix))
        {
            return new CommandOrCodeActionContainer();
        }

        var actions = new List<CommandOrCodeAction>();
        foreach (Diagnostic diagnostic in request.Context.Diagnostics)
        {
            if (!string.Equals(
                    diagnostic.Source,
                    "sharpts",
                    StringComparison.Ordinal) ||
                !TryReadReplacement(
                    diagnostic.Data,
                    out string? title,
                    out string? newText))
            {
                continue;
            }

            var edit = new TextEdit
            {
                Range = diagnostic.Range,
                NewText = newText,
            };
            actions.Add(new CodeAction
            {
                Title = title,
                Kind = CodeActionKind.QuickFix,
                IsPreferred = true,
                Diagnostics = new Container<Diagnostic>(diagnostic),
                Edit = new WorkspaceEdit
                {
                    Changes =
                        new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                        {
                            [request.TextDocument.Uri] = [edit],
                        },
                },
            });
        }

        return new CommandOrCodeActionContainer(actions);
    }

    private static bool TryReadReplacement(
        JToken? data,
        out string title,
        out string newText)
    {
        title = "";
        newText = "";
        if (data is not JObject value ||
            value.Value<int?>(InteropCodeActionMetadata.VersionKey) !=
                InteropCodeActionMetadata.CurrentVersion ||
            !string.Equals(
                value.Value<string>(InteropCodeActionMetadata.KindKey),
                InteropCodeActionMetadata.ReplaceDiagnosticRange,
                StringComparison.Ordinal))
        {
            return false;
        }

        title =
            value.Value<string>(InteropCodeActionMetadata.TitleKey) ?? "";
        newText =
            value.Value<string>(InteropCodeActionMetadata.NewTextKey) ?? "";
        return title.Length > 0;
    }
}
