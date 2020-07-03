// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591
#pragma warning disable CA1823 // Unused field
#nullable enable

namespace BuildXL.FrontEnd.Script.Analyzer.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("BxlScriptAnalayzerLogger")]
    public abstract partial class Logger : LoggerBase
    {
        private const int DefaultKeywords = (int)(Keywords.UserMessage | Keywords.Diagnostics);

        // Internal logger will prevent public users from creating an instance of the logger
        internal Logger()
        {
        }

       
        
        [GeneratedEvent(
            (ushort)LogEventId.ErrorParsingFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Error loading configuration.",
            Keywords = DefaultKeywords)]
        public abstract void ErrorParsingFile(LoggingContext context, Location location);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorParsingFilter,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Error at position {position} of command line pip filter {filter}. {message} {positionMarker}",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void ErrorParsingFilter(LoggingContext context, string filter, int position, string message, string positionMarker);

        [GeneratedEvent(
            (ushort)LogEventId.ErrorFilterHasNoMatchingSpecs,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Filter: {filter} resulted in no specs being selected to be fixed.",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void ErrorFilterHasNoMatchingSpecs(LoggingContext context, string filter);

        [GeneratedEvent(
            (ushort)LogEventId.FixRequiresPrettyPrint,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "When you pass '/Fix' you should add the PrettyPrint analyzer using '/a:PrettyPrint' as the last argument to ensure the fixes are written to disk.",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void FixRequiresPrettyPrint(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.AnalysisErrorSummary,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Encountered {nrOfErrors} errors using {nrOfAnalyzers}. Pass '/Fix' to automatically apply the fixes.",
            Keywords = DefaultKeywords)]
        public abstract void AnalysisErrorSummary(LoggingContext context, int nrOfErrors, int nrOfAnalyzers);

        #region PrettyPrint

        [GeneratedEvent(
            (ushort)LogEventId.PrettyPrintErrorWritingSpecFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Failed to write updates back to the file: {message}.",
            Keywords = DefaultKeywords)]
        public abstract void PrettyPrintErrorWritingSpecFile(LoggingContext context, Location location, string message);

        [GeneratedEvent(
            (ushort)LogEventId.PrettyPrintUnexpectedChar,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Non-standard formatting encountered. Encountered: '{encounteredToken}' expected: '{expectedToken}' in line:\r\n{encounteredLine}\r\n{positionMarker}",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void PrettyPrintUnexpectedChar(LoggingContext context, Location location, string expectedToken, string encounteredToken, string encounteredLine, string positionMarker);

        [GeneratedEvent(
            (ushort)LogEventId.PrettyPrintExtraTargetLines,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Non-standard formatting encountered. Encountered a missing line. Expected line:\r\n{expectedLine}",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void PrettyPrintExtraTargetLines(LoggingContext context, Location location, string expectedLine);

        [GeneratedEvent(
            (ushort)LogEventId.PrettyPrintExtraSourceLines,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Non-standard formatting encountered. Encountered an extra line:\r\n{encountered}",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void PrettyPrintExtraSourceLines(LoggingContext context, Location location, string encountered);

        #endregion

        #region LegacyLiteralCreation

        [GeneratedEvent(
            (ushort)LogEventId.LegacyLiteralFix,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Use {fixExpression} rather than {existingExpression}",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void LegacyLiteralFix(LoggingContext context, Location location, string fixExpression, string existingExpression);

        #endregion

        #region PathFixer

        [GeneratedEvent(
            (ushort)LogEventId.PathFixerIllegalPathSeparator,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Use path separator '{expectedSeparator}' rather than '{illegalSeparator}' in '{pathFragment}'",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void PathFixerIllegalPathSeparator(LoggingContext context, Location location, string pathFragment, char expectedSeparator, char illegalSeparator);

        [GeneratedEvent(
            (ushort)LogEventId.PathFixerIllegalCasing,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = EventConstants.LabeledProvenancePrefix + "Use lowercase for all directory parts. Use '{expectedLoweredFragment}' rather than '{encounteredFragment}' in '{pathFragment}'.",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void PathFixerIllegalCasing(LoggingContext context, Location location, string pathFragment, string encounteredFragment, string expectedLoweredFragment);

        #endregion

        #region Documentation

        [GeneratedEvent(
            (ushort)LogEventId.DocumentationMissingOutputFolder,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "The Documentation Analyzer requires the parameter '{parameter}'. None was given.",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void DocumentationMissingOutputFolder(LoggingContext context, string parameter);

        [GeneratedEvent(
            (ushort)LogEventId.DocumentationErrorCleaningFolder,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Error cleaning output folder '{outputFolder}': {message}",
            Keywords = DefaultKeywords)]
        public abstract void DocumentationErrorCleaningFolder(LoggingContext context, string outputFolder, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DocumentationErrorCreatingOutputFolder,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Error creating output folder '{outputFolder}': {message}",
            Keywords = DefaultKeywords)]
        public abstract void DocumentationErrorCreatingOutputFolder(LoggingContext context, string outputFolder, string message);

        [GeneratedEvent(
            (ushort)LogEventId.DocumentationSkippingV1Module,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Skipping module '{moduleName}' because it is not a v2 module.",
            Keywords = DefaultKeywords)]
        public abstract void DocumentationSkippingV1Module(LoggingContext context, string moduleName);

        #endregion

        #region Graph fragment

        [GeneratedEvent(
            (ushort)LogEventId.GraphFragmentMissingOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "The GraphFragment analyzer requires the parameter '{parameter}'. None was given.",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void GraphFragmentMissingOutputFile(LoggingContext context, string parameter);

        [GeneratedEvent(
            (ushort)LogEventId.GraphFragmentInvalidOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "The GraphFragment analyzer has invalid file '{file}' for parameter '{parameter}'.",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void GraphFragmentInvalidOutputFile(LoggingContext context, string file, string parameter);

        [GeneratedEvent(
            (ushort)LogEventId.GraphFragmentMissingGraph,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "The GraphFragment analyzer requires a pip graph.",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void GraphFragmentMissingGraph(LoggingContext context);

        [GeneratedEvent(
            (ushort)LogEventId.GraphFragmentExceptionOnSerializingFragment,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "An exception occured when the GraphFragment analyzer serialized the graph fragment to '{file}': {exceptionMessage}",
            Keywords = DefaultKeywords | (int)Keywords.UserError)]
        public abstract void GraphFragmentExceptionOnSerializingFragment(LoggingContext context, string file, string exceptionMessage);

        [GeneratedEvent(
            (ushort)LogEventId.GraphFragmentSerializationStats,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            EventTask = (ushort)Tasks.Analyzers,
            Message = "Serialization stats of graph fragment '{fragmentDescription}': {stats}",
            Keywords = DefaultKeywords)]
        public abstract void GraphFragmentSerializationStats(LoggingContext context, string fragmentDescription, string stats);

        #endregion
    }
}

#pragma warning restore CA1823 // Unused field
