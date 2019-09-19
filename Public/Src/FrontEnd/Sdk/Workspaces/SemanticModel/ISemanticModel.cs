// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Types;
using ISymbol = TypeScript.Net.Types.ISymbol;

namespace BuildXL.FrontEnd.Workspaces
{
    /// <summary>
    /// Semantic model (i.e., symbol resolution and other facilities) for DScript specs.
    /// </summary>
    /// <remarks>
    /// Currently semantic model is just an adapter around <see cref="ITypeChecker"/>.
    /// <see cref="ITypeChecker"/> interface is very wide and semantic model helps to separate semantic validation
    /// from symbols computation.
    /// </remarks>
    public interface ISemanticModel
    {
        /// <summary>
        /// Exposed for simplicity reasons. Will be removed.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        ITypeChecker TypeChecker { get; }

        /// <summary>
        /// Returns all diagnostics that occurred during type checking for a given file.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        IEnumerable<Diagnostic> GetTypeCheckingDiagnosticsForFile([JetBrains.Annotations.NotNull]ISourceFile file);

        /// <summary>
        /// Returns all diagnostics that occurred during semantic binding and type checking.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        IEnumerable<Diagnostic> GetAllSemanticDiagnostics();

        /// <summary>
        /// Returns a file name that corresponds to a module referenced by <paramref name="sourceFile"/>.
        /// </summary>
        [CanBeNull]
        string TryGetResolvedModulePath([JetBrains.Annotations.NotNull] ISourceFile sourceFile, [JetBrains.Annotations.NotNull]string referencedModuleName);

        /// <summary>
        /// Returns a set of file indices that depend on the current one.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        RoaringBitSet GetFileDependentFilesOf([JetBrains.Annotations.NotNull]ISourceFile sourceFile);

        /// <summary>
        /// Returns a set of file indices that the current file depend on.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        RoaringBitSet GetFileDependenciesOf([JetBrains.Annotations.NotNull]ISourceFile sourceFile);

        /// <summary>
        /// Returns a set of modules that the current file depends on.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        HashSet<string> GetModuleDependentsOf([JetBrains.Annotations.NotNull]ISourceFile sourceFile);

        /// <summary>
        /// Returns a qualifier type for a given node.
        /// </summary>
        [CanBeNull]
        IType GetCurrentQualifierType([JetBrains.Annotations.NotNull]INode currentNode);

        /// <summary>
        /// Returns the qualifier declaration for a given node.
        /// </summary>
        [CanBeNull]
        INode GetCurrentQualifierDeclaration([JetBrains.Annotations.NotNull]INode currentNode);

        /// <summary>
        /// Returns the template symbol in a scope with respect to the given given node, or null if the template is not found.
        /// </summary>
        [CanBeNull]
        ISymbol GetTemplateAtLocation([JetBrains.Annotations.NotNull]INode node);

        /// <summary>
        /// Returns true if a given symbol points to a namespace.
        /// </summary>
        bool IsNamespaceType([JetBrains.Annotations.NotNull]ISymbol symbol);

        /// <summary>
        /// Returns true if a resolved type of a given symbol is a namespace.
        /// </summary>
        bool IsNamespaceType([JetBrains.Annotations.NotNull]INode currentNode);

        /// <summary>
        /// Returns the fully qualified name of a symbol.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        string GetFullyQualifiedName([JetBrains.Annotations.NotNull]ISymbol symbol);

        /// <summary>
        /// Resolves a symbol alias
        /// </summary>
        /// <remarks>
        /// This method recursively resolves aliased symbols until it finds a non-aliased one. This
        /// is the standard behavior of the checker. A DScript-specific functionality is available
        /// if resolveAliasRecursively is set to false so the resolution goes one hop at a time.
        /// </remarks>
        ISymbol GetAliasedSymbol([JetBrains.Annotations.NotNull]ISymbol symbol, bool resolveAliasRecursively = true);

        /// <summary>
        /// Returns a value symbol of an identifier in the short-hand property assignment.
        /// </summary>
        ISymbol GetShorthandAssignmentValueSymbol([JetBrains.Annotations.NotNull]INode location);

        /// <summary>
        /// Returns the symbol associated with a node
        /// </summary>
        ISymbol GetSymbolAtLocation([JetBrains.Annotations.NotNull]INode node);

        /// <summary>
        /// Returns the type associated to the node
        /// </summary>
        IType GetTypeAtLocation([JetBrains.Annotations.NotNull]INode node);

        /// <summary>
        /// Returns a string representation of a type if possible
        /// </summary>
        bool TryPrintType(IType type, out string result, INode enclosingDeclaration = null, TypeFormatFlags flags = TypeFormatFlags.None);

        /// <summary>
        /// Returns a string representation of the return type of a signature declaration if possible
        /// </summary>
        bool TryPrintReturnTypeOfSignature(
            [JetBrains.Annotations.NotNull]ISignatureDeclaration signatureDeclaration,
            out string result,
            INode enclosingDeclaration = null,
            TypeFormatFlags flags = TypeFormatFlags.None);

        /// <summary>
        /// Notifies a semantic model that the user filter was applied and a given set of specs were filtered out.
        /// </summary>
        void FilterWasApplied([JetBrains.Annotations.NotNull]HashSet<ISourceFile> filteredOutSpecs);

        /// <summary>
        /// Returns a first declaration for a given symbol.
        /// </summary>
        /// <remarks>
        /// Unlike <code>resolvedSymbol.Declarations.FirstOrDefault()</code> this method knows about declarations that were filtered out and will never
        /// return one of it.
        /// </remarks>
        [CanBeNull]
        IDeclaration GetFirstNotFilteredDeclarationOrDefault([CanBeNull]ISymbol resolvedSymbol);
    }

    /// <summary>
    /// Extension methods for <see cref="ISemanticModel"/>.
    /// </summary>
    public static class SemanticModelExtensions
    {
        /// <summary>
        /// Returns true if there was no issues building semantic model.
        /// </summary>
        public static bool Success([JetBrains.Annotations.NotNull]this ISemanticModel semanticModel)
        {
            return !semanticModel.GetAllSemanticDiagnostics().Any();
        }
    }
}
