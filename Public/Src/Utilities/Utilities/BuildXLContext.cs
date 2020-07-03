// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Core container of context objects
    /// </summary>
    public abstract class BuildXLContext : PipExecutionContext
    {
        private readonly TokenTextTable m_tokenTextTable;

        /// <summary>
        /// protected constructor
        /// </summary>
        protected BuildXLContext(BuildXLContext context)
            : this(
            context.CancellationToken,
            context.StringTable,
            context.PathTable,
            context.SymbolTable,
            context.QualifierTable,
            context.TokenTextTable)
        {
            Contract.RequiresNotNull(context);
        }

        /// <summary>
        /// Must create a derived class for your component
        /// </summary>
        protected BuildXLContext(
            CancellationToken cancellationToken,
            StringTable stringTable,
            PathTable pathTable,
            SymbolTable symbolTable,
            QualifierTable qualifierTable,
            TokenTextTable tokenTextTable)
            : base(
                cancellationToken,
                stringTable,
                pathTable,
                symbolTable,
                qualifierTable)
        {
            Contract.RequiresNotNull(tokenTextTable);

            m_tokenTextTable = tokenTextTable;
        }

        /// <summary>
        /// Creates a new context for testing purposes only. Real components should create a derived class
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "BuildXLContext takes ownership for disposal.")]
        public static BuildXLContext CreateInstanceForTesting()
        {
            var stringTable = new StringTable();
            var pathTable = new PathTable(stringTable);
            var symbolTable = new SymbolTable(stringTable);
            var qualifierTable = new QualifierTable(stringTable);
            var tokenTextTable = new TokenTextTable();

            return new BuildXLTestContext(stringTable, pathTable, symbolTable, qualifierTable, tokenTextTable, CancellationToken.None);
        }

        /// <summary>
        /// Creates a new context for testing purposes only using existing context and cancellation token
        /// Real components should create a derived class
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "BuildXLContext takes ownership for disposal.")]
        public static BuildXLContext CreateInstanceForTestingWithCancellationToken(BuildXLContext context, CancellationToken cancellationToken)
        {
            return new BuildXLTestContext(context.StringTable, context.PathTable, context.SymbolTable, context.QualifierTable, context.TokenTextTable, cancellationToken);
        }

        /// <summary>
        /// TokenTextTable for this invocation;
        /// </summary>
        public TokenTextTable TokenTextTable
        {
            get
            {
                return m_tokenTextTable;
            }
        }

        /// <summary>
        /// Invalidates the context to prevent future use
        /// </summary>
        public virtual void Invalidate()
        {
            StringTable.Invalidate();
            PathTable.Invalidate();
            SymbolTable.Invalidate();
            TokenTextTable.Invalidate();
        }

        /// <summary>
        /// Private class for testing purposes
        /// </summary>
        private sealed class BuildXLTestContext : BuildXLContext
        {
            /// <summary>
            /// Constructs a new instance
            /// </summary>
            public BuildXLTestContext(
                StringTable stringTable,
                PathTable pathTable,
                SymbolTable symbolTable,
                QualifierTable qualifierTable,
                TokenTextTable tokenTextTable,
                CancellationToken cancellationToken)
                : base(
                    cancellationToken,
                    stringTable,
                    pathTable,
                    symbolTable,
                    qualifierTable,
                    tokenTextTable)
            {
            }
        }
    }
}
