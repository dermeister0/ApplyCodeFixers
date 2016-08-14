using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using StyleCop.Analyzers;
using StyleCop.Analyzers.Helpers;

namespace AbbreviationFix
{
    /// <summary>
    /// Abbreviations are not allowed except officially register. Apply common naming rules for them
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, LanguageNames.VisualBasic, Name = nameof(AbbreviationVBFixProvider))]
    [Shared]
    public class AbbreviationVBFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(AbbreviationVBAnalyzer.DiagnosticId); }
        }

        /// <inheritdoc/>
        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        /// <inheritdoc/>
        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var document = context.Document;
            var settings = document.Project.AnalyzerOptions.GetStyleCopSettings(context.CancellationToken);
            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            foreach (var diagnostic in context.Diagnostics)
            {
                var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
                var originalName = token.ValueText;
                var memberSyntax = RenameVBHelper.GetParentDeclaration(token);

                var newName = AbbreviationHelper.RenameAll(originalName);

                if (memberSyntax != null)
                {
                    /*
                     * VB.NET - don't check existing members, because we change case only.
                     */

                    Debug.WriteLine("{0}|{1}|{2}", token.ValueText, newName, document.Name);
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            string.Format("Rename to {0}", newName),
                            cancellationToken => RenameHelper.RenameSymbolAsync(document, root, token, newName, cancellationToken),
                            nameof(AbbreviationVBFixProvider) + "_" + diagnostic.Id),
                        diagnostic);
                }
            }
        }
    }
}
