//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     ANTLR Version: 4.7.2
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

// Generated from JPath.g4 by ANTLR 4.7.2

// Unreachable code detected
#pragma warning disable 0162
// The variable '...' is assigned but its value is never used
#pragma warning disable 0219
// Missing XML comment for publicly visible type or member '...'
#pragma warning disable 1591
// Ambiguous reference in cref attribute
#pragma warning disable 419

namespace BuildXL.Execution.Analyzer.JPath {
using Antlr4.Runtime.Misc;
using IParseTreeListener = Antlr4.Runtime.Tree.IParseTreeListener;
using IToken = Antlr4.Runtime.IToken;

/// <summary>
/// This interface defines a complete listener for a parse tree produced by
/// <see cref="JPathParser"/>.
/// </summary>
[System.CodeDom.Compiler.GeneratedCode("ANTLR", "4.7.2")]
[System.CLSCompliant(false)]
public interface IJPathListener : IParseTreeListener {
	/// <summary>
	/// Enter a parse tree produced by <see cref="JPathParser.intBinaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterIntBinaryOp([JetBrains.Annotations.NotNull] JPathParser.IntBinaryOpContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="JPathParser.intBinaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitIntBinaryOp([JetBrains.Annotations.NotNull] JPathParser.IntBinaryOpContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="JPathParser.intUnaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterIntUnaryOp([JetBrains.Annotations.NotNull] JPathParser.IntUnaryOpContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="JPathParser.intUnaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitIntUnaryOp([JetBrains.Annotations.NotNull] JPathParser.IntUnaryOpContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="JPathParser.boolBinaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBoolBinaryOp([JetBrains.Annotations.NotNull] JPathParser.BoolBinaryOpContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="JPathParser.boolBinaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBoolBinaryOp([JetBrains.Annotations.NotNull] JPathParser.BoolBinaryOpContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="JPathParser.logicBinaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterLogicBinaryOp([JetBrains.Annotations.NotNull] JPathParser.LogicBinaryOpContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="JPathParser.logicBinaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitLogicBinaryOp([JetBrains.Annotations.NotNull] JPathParser.LogicBinaryOpContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="JPathParser.logicUnaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterLogicUnaryOp([JetBrains.Annotations.NotNull] JPathParser.LogicUnaryOpContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="JPathParser.logicUnaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitLogicUnaryOp([JetBrains.Annotations.NotNull] JPathParser.LogicUnaryOpContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="JPathParser.arrayBinaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterArrayBinaryOp([JetBrains.Annotations.NotNull] JPathParser.ArrayBinaryOpContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="JPathParser.arrayBinaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitArrayBinaryOp([JetBrains.Annotations.NotNull] JPathParser.ArrayBinaryOpContext context);
	/// <summary>
	/// Enter a parse tree produced by <see cref="JPathParser.anyBinaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterAnyBinaryOp([JetBrains.Annotations.NotNull] JPathParser.AnyBinaryOpContext context);
	/// <summary>
	/// Exit a parse tree produced by <see cref="JPathParser.anyBinaryOp"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitAnyBinaryOp([JetBrains.Annotations.NotNull] JPathParser.AnyBinaryOpContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>BinaryIntExpr</c>
	/// labeled alternative in <see cref="JPathParser.intExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBinaryIntExpr([JetBrains.Annotations.NotNull] JPathParser.BinaryIntExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>BinaryIntExpr</c>
	/// labeled alternative in <see cref="JPathParser.intExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBinaryIntExpr([JetBrains.Annotations.NotNull] JPathParser.BinaryIntExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>ExprIntExpr</c>
	/// labeled alternative in <see cref="JPathParser.intExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterExprIntExpr([JetBrains.Annotations.NotNull] JPathParser.ExprIntExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>ExprIntExpr</c>
	/// labeled alternative in <see cref="JPathParser.intExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitExprIntExpr([JetBrains.Annotations.NotNull] JPathParser.ExprIntExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>UnaryIntExpr</c>
	/// labeled alternative in <see cref="JPathParser.intExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterUnaryIntExpr([JetBrains.Annotations.NotNull] JPathParser.UnaryIntExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>UnaryIntExpr</c>
	/// labeled alternative in <see cref="JPathParser.intExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitUnaryIntExpr([JetBrains.Annotations.NotNull] JPathParser.UnaryIntExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>SubIntExpr</c>
	/// labeled alternative in <see cref="JPathParser.intExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSubIntExpr([JetBrains.Annotations.NotNull] JPathParser.SubIntExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>SubIntExpr</c>
	/// labeled alternative in <see cref="JPathParser.intExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSubIntExpr([JetBrains.Annotations.NotNull] JPathParser.SubIntExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>BinaryBoolExpr</c>
	/// labeled alternative in <see cref="JPathParser.boolExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBinaryBoolExpr([JetBrains.Annotations.NotNull] JPathParser.BinaryBoolExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>BinaryBoolExpr</c>
	/// labeled alternative in <see cref="JPathParser.boolExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBinaryBoolExpr([JetBrains.Annotations.NotNull] JPathParser.BinaryBoolExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>SubBoolExpr</c>
	/// labeled alternative in <see cref="JPathParser.boolExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSubBoolExpr([JetBrains.Annotations.NotNull] JPathParser.SubBoolExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>SubBoolExpr</c>
	/// labeled alternative in <see cref="JPathParser.boolExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSubBoolExpr([JetBrains.Annotations.NotNull] JPathParser.SubBoolExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>BoolLogicExpr</c>
	/// labeled alternative in <see cref="JPathParser.logicExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBoolLogicExpr([JetBrains.Annotations.NotNull] JPathParser.BoolLogicExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>BoolLogicExpr</c>
	/// labeled alternative in <see cref="JPathParser.logicExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBoolLogicExpr([JetBrains.Annotations.NotNull] JPathParser.BoolLogicExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>UnaryLogicExpr</c>
	/// labeled alternative in <see cref="JPathParser.logicExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterUnaryLogicExpr([JetBrains.Annotations.NotNull] JPathParser.UnaryLogicExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>UnaryLogicExpr</c>
	/// labeled alternative in <see cref="JPathParser.logicExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitUnaryLogicExpr([JetBrains.Annotations.NotNull] JPathParser.UnaryLogicExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>SubLogicExpr</c>
	/// labeled alternative in <see cref="JPathParser.logicExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSubLogicExpr([JetBrains.Annotations.NotNull] JPathParser.SubLogicExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>SubLogicExpr</c>
	/// labeled alternative in <see cref="JPathParser.logicExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSubLogicExpr([JetBrains.Annotations.NotNull] JPathParser.SubLogicExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>BinaryLogicExpr</c>
	/// labeled alternative in <see cref="JPathParser.logicExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBinaryLogicExpr([JetBrains.Annotations.NotNull] JPathParser.BinaryLogicExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>BinaryLogicExpr</c>
	/// labeled alternative in <see cref="JPathParser.logicExpr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBinaryLogicExpr([JetBrains.Annotations.NotNull] JPathParser.BinaryLogicExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>PropertyId</c>
	/// labeled alternative in <see cref="JPathParser.prop"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterPropertyId([JetBrains.Annotations.NotNull] JPathParser.PropertyIdContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>PropertyId</c>
	/// labeled alternative in <see cref="JPathParser.prop"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitPropertyId([JetBrains.Annotations.NotNull] JPathParser.PropertyIdContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>EscId</c>
	/// labeled alternative in <see cref="JPathParser.prop"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterEscId([JetBrains.Annotations.NotNull] JPathParser.EscIdContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>EscId</c>
	/// labeled alternative in <see cref="JPathParser.prop"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitEscId([JetBrains.Annotations.NotNull] JPathParser.EscIdContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>IdSelector</c>
	/// labeled alternative in <see cref="JPathParser.selector"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterIdSelector([JetBrains.Annotations.NotNull] JPathParser.IdSelectorContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>IdSelector</c>
	/// labeled alternative in <see cref="JPathParser.selector"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitIdSelector([JetBrains.Annotations.NotNull] JPathParser.IdSelectorContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>UnionSelector</c>
	/// labeled alternative in <see cref="JPathParser.selector"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterUnionSelector([JetBrains.Annotations.NotNull] JPathParser.UnionSelectorContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>UnionSelector</c>
	/// labeled alternative in <see cref="JPathParser.selector"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitUnionSelector([JetBrains.Annotations.NotNull] JPathParser.UnionSelectorContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>StrLitExpr</c>
	/// labeled alternative in <see cref="JPathParser.literal"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterStrLitExpr([JetBrains.Annotations.NotNull] JPathParser.StrLitExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>StrLitExpr</c>
	/// labeled alternative in <see cref="JPathParser.literal"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitStrLitExpr([JetBrains.Annotations.NotNull] JPathParser.StrLitExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>RegExLitExpr</c>
	/// labeled alternative in <see cref="JPathParser.literal"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterRegExLitExpr([JetBrains.Annotations.NotNull] JPathParser.RegExLitExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>RegExLitExpr</c>
	/// labeled alternative in <see cref="JPathParser.literal"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitRegExLitExpr([JetBrains.Annotations.NotNull] JPathParser.RegExLitExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>IntLitExpr</c>
	/// labeled alternative in <see cref="JPathParser.literal"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterIntLitExpr([JetBrains.Annotations.NotNull] JPathParser.IntLitExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>IntLitExpr</c>
	/// labeled alternative in <see cref="JPathParser.literal"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitIntLitExpr([JetBrains.Annotations.NotNull] JPathParser.IntLitExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>MapExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterMapExpr([JetBrains.Annotations.NotNull] JPathParser.MapExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>MapExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitMapExpr([JetBrains.Annotations.NotNull] JPathParser.MapExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>FuncOptExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterFuncOptExpr([JetBrains.Annotations.NotNull] JPathParser.FuncOptExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>FuncOptExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitFuncOptExpr([JetBrains.Annotations.NotNull] JPathParser.FuncOptExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>CardinalityExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterCardinalityExpr([JetBrains.Annotations.NotNull] JPathParser.CardinalityExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>CardinalityExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitCardinalityExpr([JetBrains.Annotations.NotNull] JPathParser.CardinalityExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>LetExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterLetExpr([JetBrains.Annotations.NotNull] JPathParser.LetExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>LetExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitLetExpr([JetBrains.Annotations.NotNull] JPathParser.LetExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>SubExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSubExpr([JetBrains.Annotations.NotNull] JPathParser.SubExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>SubExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSubExpr([JetBrains.Annotations.NotNull] JPathParser.SubExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>BinExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterBinExpr([JetBrains.Annotations.NotNull] JPathParser.BinExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>BinExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitBinExpr([JetBrains.Annotations.NotNull] JPathParser.BinExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>RangeExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterRangeExpr([JetBrains.Annotations.NotNull] JPathParser.RangeExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>RangeExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitRangeExpr([JetBrains.Annotations.NotNull] JPathParser.RangeExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>IndexExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterIndexExpr([JetBrains.Annotations.NotNull] JPathParser.IndexExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>IndexExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitIndexExpr([JetBrains.Annotations.NotNull] JPathParser.IndexExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>AssignExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterAssignExpr([JetBrains.Annotations.NotNull] JPathParser.AssignExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>AssignExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitAssignExpr([JetBrains.Annotations.NotNull] JPathParser.AssignExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>SelectorExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterSelectorExpr([JetBrains.Annotations.NotNull] JPathParser.SelectorExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>SelectorExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitSelectorExpr([JetBrains.Annotations.NotNull] JPathParser.SelectorExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>FilterExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterFilterExpr([JetBrains.Annotations.NotNull] JPathParser.FilterExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>FilterExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitFilterExpr([JetBrains.Annotations.NotNull] JPathParser.FilterExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>RootExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterRootExpr([JetBrains.Annotations.NotNull] JPathParser.RootExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>RootExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitRootExpr([JetBrains.Annotations.NotNull] JPathParser.RootExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>PipeExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterPipeExpr([JetBrains.Annotations.NotNull] JPathParser.PipeExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>PipeExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitPipeExpr([JetBrains.Annotations.NotNull] JPathParser.PipeExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>VarExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterVarExpr([JetBrains.Annotations.NotNull] JPathParser.VarExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>VarExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitVarExpr([JetBrains.Annotations.NotNull] JPathParser.VarExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>LiteralExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterLiteralExpr([JetBrains.Annotations.NotNull] JPathParser.LiteralExprContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>LiteralExpr</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitLiteralExpr([JetBrains.Annotations.NotNull] JPathParser.LiteralExprContext context);
	/// <summary>
	/// Enter a parse tree produced by the <c>FuncAppExprParen</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void EnterFuncAppExprParen([JetBrains.Annotations.NotNull] JPathParser.FuncAppExprParenContext context);
	/// <summary>
	/// Exit a parse tree produced by the <c>FuncAppExprParen</c>
	/// labeled alternative in <see cref="JPathParser.expr"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	void ExitFuncAppExprParen([JetBrains.Annotations.NotNull] JPathParser.FuncAppExprParenContext context);
}
} // namespace BuildXL.Execution.Analyzer.JPath
