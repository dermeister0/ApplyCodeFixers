using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AbbreviationFix
{
    using System;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;
    using StyleCop.Analyzers.Helpers;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class AbbreviationFixAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AbbreviationFixAnalyzer";
        private const string Title = "Abbreviations are not allowed except officially registered";
        private const string MessageFormat = "Element '{0}'  must not contain series of capital letters";
        private const string Description = "";
        private const string HelpLink = "";

        private static readonly DiagnosticDescriptor Descriptor =
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, "NamingRules", DiagnosticSeverity.Warning, true, Description, HelpLink);

        private static readonly Action<CompilationStartAnalysisContext> CompilationStartAction = HandleCompilationStart;
        private static readonly Action<SyntaxNodeAnalysisContext> ClassDeclarationAction = HandleClassDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext> InterfaceDeclarationAction = HandleInterfaceDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext> EnumDeclarationAction = HandleEnumDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext> EnumMemberDeclarationAction = HandleEnumMemberDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext> StructDeclarationAction = HandleStructDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext> DelegateDeclarationAction = HandleDelegateDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext> EventDeclarationAction = HandleEventDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext> EventFieldDeclarationAction = HandleEventFieldDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext> MethodDeclarationAction = HandleMethodDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext> PropertyDeclarationAction = HandlePropertyDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext> VariableDeclarationAction = HandleVariableDeclaration;

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptor);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.RegisterCompilationStartAction(CompilationStartAction);
        }

        private static void HandleCompilationStart(CompilationStartAnalysisContext context)
        {
            context.RegisterSyntaxNodeActionHonorExclusions(InterfaceDeclarationAction, SyntaxKind.InterfaceDeclaration);
            context.RegisterSyntaxNodeActionHonorExclusions(ClassDeclarationAction, SyntaxKind.ClassDeclaration);
            context.RegisterSyntaxNodeActionHonorExclusions(EnumDeclarationAction, SyntaxKind.EnumDeclaration);
            context.RegisterSyntaxNodeActionHonorExclusions(EnumMemberDeclarationAction, SyntaxKind.EnumMemberDeclaration);
            context.RegisterSyntaxNodeActionHonorExclusions(StructDeclarationAction, SyntaxKind.StructDeclaration);
            context.RegisterSyntaxNodeActionHonorExclusions(DelegateDeclarationAction, SyntaxKind.DelegateDeclaration);
            context.RegisterSyntaxNodeActionHonorExclusions(EventDeclarationAction, SyntaxKind.EventDeclaration);
            context.RegisterSyntaxNodeActionHonorExclusions(EventFieldDeclarationAction, SyntaxKind.EventFieldDeclaration);
            context.RegisterSyntaxNodeActionHonorExclusions(MethodDeclarationAction, SyntaxKind.MethodDeclaration);
            context.RegisterSyntaxNodeActionHonorExclusions(PropertyDeclarationAction, SyntaxKind.PropertyDeclaration);
            context.RegisterSyntaxNodeActionHonorExclusions(VariableDeclarationAction, SyntaxKind.VariableDeclaration);
        }

        private static void HandleVariableDeclaration(SyntaxNodeAnalysisContext context)
        {
            VariableDeclarationSyntax syntax = (VariableDeclarationSyntax)context.Node;
            if (syntax.Parent.IsKind(SyntaxKind.EventFieldDeclaration))
            {
                // This diagnostic is only for local variables.
                return;
            }

            if (NamedTypeHelpers.IsContainedInNativeMethodsClass(syntax))
            {
                return;
            }

            foreach (VariableDeclaratorSyntax variableDeclarator in syntax.Variables)
            {
                if (variableDeclarator == null)
                {
                    continue;
                }

                CheckElementNameToken(context, variableDeclarator.Identifier);
            }
        }

        private static void CheckNameSyntax(SyntaxNodeAnalysisContext context, NameSyntax nameSyntax)
        {
            if (nameSyntax == null || nameSyntax.IsMissing)
            {
                return;
            }

            QualifiedNameSyntax qualifiedNameSyntax = nameSyntax as QualifiedNameSyntax;
            if (qualifiedNameSyntax != null)
            {
                CheckNameSyntax(context, qualifiedNameSyntax.Left);
                CheckNameSyntax(context, qualifiedNameSyntax.Right);
                return;
            }

            SimpleNameSyntax simpleNameSyntax = nameSyntax as SimpleNameSyntax;
            if (simpleNameSyntax != null)
            {
                CheckElementNameToken(context, simpleNameSyntax.Identifier);
                return;
            }

            // TODO: any other cases?
        }

        private static void HandleClassDeclaration(SyntaxNodeAnalysisContext context)
        {
            CheckElementNameToken(context, ((ClassDeclarationSyntax)context.Node).Identifier);
        }

        private static void HandleInterfaceDeclaration(SyntaxNodeAnalysisContext context)
        {
            CheckElementNameToken(context, ((InterfaceDeclarationSyntax)context.Node).Identifier);
        }

        private static void HandleEnumDeclaration(SyntaxNodeAnalysisContext context)
        {
            CheckElementNameToken(context, ((EnumDeclarationSyntax)context.Node).Identifier);
        }

        private static void HandleEnumMemberDeclaration(SyntaxNodeAnalysisContext context)
        {
            CheckElementNameToken(context, ((EnumMemberDeclarationSyntax)context.Node).Identifier);
        }

        private static void HandleStructDeclaration(SyntaxNodeAnalysisContext context)
        {
            CheckElementNameToken(context, ((StructDeclarationSyntax)context.Node).Identifier);
        }

        private static void HandleDelegateDeclaration(SyntaxNodeAnalysisContext context)
        {
            CheckElementNameToken(context, ((DelegateDeclarationSyntax)context.Node).Identifier);
        }

        private static void HandleEventDeclaration(SyntaxNodeAnalysisContext context)
        {
            var eventDeclaration = (EventDeclarationSyntax)context.Node;
            if (eventDeclaration.Modifiers.Any(SyntaxKind.OverrideKeyword))
            {
                // Don't analyze an overridden event.
                return;
            }

            CheckElementNameToken(context, eventDeclaration.Identifier);
        }

        private static void HandleEventFieldDeclaration(SyntaxNodeAnalysisContext context)
        {
            EventFieldDeclarationSyntax eventFieldDeclarationSyntax = (EventFieldDeclarationSyntax)context.Node;
            VariableDeclarationSyntax variableDeclarationSyntax = eventFieldDeclarationSyntax.Declaration;
            if (variableDeclarationSyntax == null || variableDeclarationSyntax.IsMissing)
            {
                return;
            }

            foreach (var declarator in variableDeclarationSyntax.Variables)
            {
                if (declarator == null || declarator.IsMissing)
                {
                    continue;
                }

                CheckElementNameToken(context, declarator.Identifier);
            }
        }

        private static void HandleMethodDeclaration(SyntaxNodeAnalysisContext context)
        {
            var methodDeclaration = (MethodDeclarationSyntax)context.Node;
            if (methodDeclaration.Modifiers.Any(SyntaxKind.OverrideKeyword))
            {
                // Don't analyze an overridden method.
                return;
            }

            CheckElementNameToken(context, methodDeclaration.Identifier);
        }

        private static void HandlePropertyDeclaration(SyntaxNodeAnalysisContext context)
        {
            var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;
            if (propertyDeclaration.Modifiers.Any(SyntaxKind.OverrideKeyword))
            {
                // Don't analyze an overridden property.
                return;
            }

            CheckElementNameToken(context, propertyDeclaration.Identifier);
        }

        private static bool IsIdentifierValid(SyntaxToken identifier)
        {
            return !identifier.IsMissing && !string.IsNullOrEmpty(identifier.ValueText);
        }

        private static void CheckElementNameToken(SyntaxNodeAnalysisContext context, SyntaxToken identifier)
        {
            if (!IsIdentifierValid(identifier))
            {
                return;
            }

            IEnumerable<Match> matches = AbbreviationHelper.GetAbbreviationsInSymbol(identifier);
            if (!matches.Any())
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(Descriptor, identifier.GetLocation(), identifier.ValueText));
        }
    }
}
