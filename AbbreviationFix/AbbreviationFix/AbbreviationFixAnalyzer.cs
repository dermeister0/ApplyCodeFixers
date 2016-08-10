using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StyleCop.Analyzers;
using StyleCop.Analyzers.Settings.ObjectModel;

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
        private static readonly Action<SyntaxNodeAnalysisContext, StyleCopSettings> ClassDeclarationAction = HandleClassDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext, StyleCopSettings> InterfaceDeclarationAction = HandleInterfaceDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext, StyleCopSettings> EnumDeclarationAction = HandleEnumDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext, StyleCopSettings> EnumMemberDeclarationAction = HandleEnumMemberDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext, StyleCopSettings> StructDeclarationAction = HandleStructDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext, StyleCopSettings> DelegateDeclarationAction = HandleDelegateDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext, StyleCopSettings> EventDeclarationAction = HandleEventDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext, StyleCopSettings> EventFieldDeclarationAction = HandleEventFieldDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext, StyleCopSettings> MethodDeclarationAction = HandleMethodDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext, StyleCopSettings> PropertyDeclarationAction = HandlePropertyDeclaration;
        private static readonly Action<SyntaxNodeAnalysisContext, StyleCopSettings> VariableDeclarationAction = HandleVariableDeclaration;

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptor);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.RegisterCompilationStartAction(CompilationStartAction);
        }

        private static void HandleCompilationStart(CompilationStartAnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(InterfaceDeclarationAction, SyntaxKind.InterfaceDeclaration);
            context.RegisterSyntaxNodeAction(ClassDeclarationAction, SyntaxKind.ClassDeclaration);
            context.RegisterSyntaxNodeAction(EnumDeclarationAction, SyntaxKind.EnumDeclaration);
            context.RegisterSyntaxNodeAction(EnumMemberDeclarationAction, SyntaxKind.EnumMemberDeclaration);
            context.RegisterSyntaxNodeAction(StructDeclarationAction, SyntaxKind.StructDeclaration);
            context.RegisterSyntaxNodeAction(DelegateDeclarationAction, SyntaxKind.DelegateDeclaration);
            context.RegisterSyntaxNodeAction(EventDeclarationAction, SyntaxKind.EventDeclaration);
            context.RegisterSyntaxNodeAction(EventFieldDeclarationAction, SyntaxKind.EventFieldDeclaration);
            context.RegisterSyntaxNodeAction(MethodDeclarationAction, SyntaxKind.MethodDeclaration);
            context.RegisterSyntaxNodeAction(PropertyDeclarationAction, SyntaxKind.PropertyDeclaration);
            context.RegisterSyntaxNodeAction(VariableDeclarationAction, SyntaxKind.VariableDeclaration);
        }

        private static void HandleVariableDeclaration(SyntaxNodeAnalysisContext context, StyleCopSettings settings)
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

                CheckElementNameToken(context, variableDeclarator.Identifier, settings.AbbreviationRules);
            }
        }

        private static void HandleClassDeclaration(SyntaxNodeAnalysisContext context, StyleCopSettings settings)
        {
            CheckElementNameToken(context, ((ClassDeclarationSyntax)context.Node).Identifier, settings.AbbreviationRules);
        }

        private static void HandleInterfaceDeclaration(SyntaxNodeAnalysisContext context, StyleCopSettings settings)
        {
            CheckElementNameToken(context, ((InterfaceDeclarationSyntax)context.Node).Identifier, settings.AbbreviationRules);
        }

        private static void HandleEnumDeclaration(SyntaxNodeAnalysisContext context, StyleCopSettings settings)
        {
            CheckElementNameToken(context, ((EnumDeclarationSyntax)context.Node).Identifier, settings.AbbreviationRules);
        }

        private static void HandleEnumMemberDeclaration(SyntaxNodeAnalysisContext context, StyleCopSettings settings)
        {
            CheckElementNameToken(context, ((EnumMemberDeclarationSyntax)context.Node).Identifier, settings.AbbreviationRules);
        }

        private static void HandleStructDeclaration(SyntaxNodeAnalysisContext context, StyleCopSettings settings)
        {
            CheckElementNameToken(context, ((StructDeclarationSyntax)context.Node).Identifier, settings.AbbreviationRules);
        }

        private static void HandleDelegateDeclaration(SyntaxNodeAnalysisContext context, StyleCopSettings settings)
        {
            CheckElementNameToken(context, ((DelegateDeclarationSyntax)context.Node).Identifier, settings.AbbreviationRules);
        }

        private static void HandleEventDeclaration(SyntaxNodeAnalysisContext context, StyleCopSettings settings)
        {
            var eventDeclaration = (EventDeclarationSyntax)context.Node;
            if (eventDeclaration.Modifiers.Any(SyntaxKind.OverrideKeyword))
            {
                // Don't analyze an overridden event.
                return;
            }

            CheckElementNameToken(context, eventDeclaration.Identifier, settings.AbbreviationRules);
        }

        private static void HandleEventFieldDeclaration(SyntaxNodeAnalysisContext context, StyleCopSettings settings)
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

                CheckElementNameToken(context, declarator.Identifier, settings.AbbreviationRules);
            }
        }

        private static void HandleMethodDeclaration(SyntaxNodeAnalysisContext context, StyleCopSettings settings)
        {
            var methodDeclaration = (MethodDeclarationSyntax)context.Node;
            if (methodDeclaration.Modifiers.Any(SyntaxKind.OverrideKeyword))
            {
                // Don't analyze an overridden method.
                return;
            }

            CheckElementNameToken(context, methodDeclaration.Identifier, settings.AbbreviationRules);
        }

        private static void HandlePropertyDeclaration(SyntaxNodeAnalysisContext context, StyleCopSettings settings)
        {
            var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;
            if (propertyDeclaration.Modifiers.Any(SyntaxKind.OverrideKeyword))
            {
                // Don't analyze an overridden property.
                return;
            }

            CheckElementNameToken(context, propertyDeclaration.Identifier, settings.AbbreviationRules);
        }

        private static bool IsIdentifierValid(SyntaxToken identifier)
        {
            return !identifier.IsMissing && !string.IsNullOrEmpty(identifier.ValueText);
        }

        private static void CheckElementNameToken(SyntaxNodeAnalysisContext context, SyntaxToken identifier, AbbreviationSettings settings)
        {
            if (!IsIdentifierValid(identifier))
            {
                return;
            }

            IEnumerable<Match> matches = AbbreviationHelper.GetAbbreviationsInSymbol(identifier, settings);
            if (!matches.Any())
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(Descriptor, identifier.GetLocation(), identifier.ValueText));
        }
    }
}
