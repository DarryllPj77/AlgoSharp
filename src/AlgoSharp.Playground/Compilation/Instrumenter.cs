using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace AlgoSharp.Playground.Compilation;

/// <summary>
/// Rewrites the user's syntax tree so it reports what it does to <c>AlgoSharp.Tracing.Tr</c>.
/// It only <em>adds</em> calls around the user's own statements and expressions: names,
/// logic and line numbers stay the user's. All semantic questions are asked on the original
/// nodes (the rewriter receives originals), so the analysis matches the code as written.
/// </summary>
internal sealed class Instrumenter(SemanticModel model) : CSharpSyntaxRewriter
{
    private const string Tr = "global::AlgoSharp.Tracing.Tr";

    private static readonly HashSet<string> ProbeMethods =
        ["ContainsKey", "TryGetValue", "Contains", "Remove", "Add", "TryAdd", "GetValueOrDefault"];

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static ExpressionSyntax Call(string method, params ExpressionSyntax[] args) =>
        InvocationExpression(ParseExpression($"{Tr}.{method}"), ArgumentList(SeparatedList(args.Select(Argument))));

    private static ExpressionSyntax Str(string s) => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(s));

    private static ExpressionSyntax Num(int n) => LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(n));

    private static ExpressionSyntax NamedValue(string name) =>
        TupleExpression(SeparatedList([Argument(Str(name)), Argument(CastExpression(ParseTypeName("object"), IdentifierName(name)))]));

    private static StatementSyntax Stmt(ExpressionSyntax e) => ExpressionStatement(e);

    // ------------------------------------------------------------------ methods

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
        if (node.Body is not null && visited.Body is not null)
        {
            bool isStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
            return visited.WithBody(WrapBody(node.Identifier.Text, node, node.ParameterList, visited.Body, includeThis: !isStatic));
        }
        if (node.ExpressionBody is not null && visited.ExpressionBody is not null && !IsVoid(node.ReturnType))
        {
            var expr = Call("Ret", visited.ExpressionBody.Expression, Num(Line(node.ExpressionBody)));
            return visited.WithExpressionBody(visited.ExpressionBody.WithExpression(expr));
        }
        return visited;
    }

    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        var visited = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!;
        if (node.Body is null || visited.Body is null) return visited;
        bool isStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
        return visited.WithBody(WrapBody(node.Identifier.Text, node, node.ParameterList, visited.Body, includeThis: !isStatic));
    }

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        var visited = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
        if (node.Body is not null && visited.Body is not null)
        {
            return visited.WithBody(WrapBody(node.Identifier.Text, node, node.ParameterList, visited.Body));
        }
        return visited;
    }

    private static bool IsVoid(TypeSyntax type) => type is PredefinedTypeSyntax p && p.Keyword.IsKind(SyntaxKind.VoidKeyword);

    /// <summary>{ Tr.Enter(name, line, params...); try { body } finally { Tr.Exit(); } }</summary>
    private BlockSyntax WrapBody(string name, SyntaxNode decl, ParameterListSyntax parameters, BlockSyntax body, bool includeThis = false)
    {
        var args = new List<ExpressionSyntax> { Str(name), Num(Line(decl)) };
        // Instance members: the tracer shows the object's fields (e.g. a heap kept by a design class).
        if (includeThis && decl.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is ClassDeclarationSyntax cls
            && cls.Members.OfType<FieldDeclarationSyntax>().Any(f => !f.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword) || m.IsKind(SyntaxKind.ConstKeyword))))
        {
            args.Add(TupleExpression(SeparatedList([Argument(Str("this")), Argument(CastExpression(ParseTypeName("object"), ThisExpression()))])));
        }
        foreach (var p in parameters.Parameters)
        {
            if (p.Modifiers.Any(m => m.IsKind(SyntaxKind.OutKeyword))) continue;
            if (model.GetDeclaredSymbol(p) is { Type.IsRefLikeType: true }) continue;
            args.Add(NamedValue(p.Identifier.Text));
        }
        // catch (Exception e) when (Tr.Fault(e)) { throw; } never catches: the filter only records where it happened.
        var fault = CatchClause(
            CatchDeclaration(ParseTypeName("global::System.Exception"), Identifier("__fault")),
            CatchFilterClause(Call("Fault", IdentifierName("__fault"))),
            Block(ThrowStatement()));
        return Block(
            Stmt(Call("Enter", [.. args])),
            TryStatement(body, List([fault]), FinallyClause(Block(Stmt(Call("Exit"))))));
    }

    // ------------------------------------------------------------------ statement lists

    public override SyntaxNode? VisitBlock(BlockSyntax node) => node.WithStatements(List(Instrument(node.Statements)));

    public override SyntaxNode? VisitSwitchSection(SwitchSectionSyntax node)
    {
        var labels = (SwitchSectionSyntax)base.VisitSwitchSection(node)!;
        return labels.WithStatements(List(Instrument(node.Statements)));
    }

    /// <summary>Instruments a list of original statements: Step before, Set after.</summary>
    private List<StatementSyntax> Instrument(IEnumerable<StatementSyntax> statements)
    {
        var result = new List<StatementSyntax>();
        foreach (var s in statements)
        {
            var rewritten = (StatementSyntax)Visit(s)!;
            if (NeedsStep(s)) result.Add(Stmt(Call("Step", Num(Line(s)))));
            result.Add(rewritten);
            if (s is LocalDeclarationStatementSyntax or ExpressionStatementSyntax) result.AddRange(SetsAfter(s));
        }
        return result;
    }

    private static bool NeedsStep(StatementSyntax s) => s switch
    {
        ExpressionStatementSyntax or LocalDeclarationStatementSyntax or BreakStatementSyntax
            or ContinueStatementSyntax or ThrowStatementSyntax => true,
        ReturnStatementSyntax r => r.Expression is null,
        _ => false,
    };

    /// <summary>A block for an embedded statement (if/loop bodies written without braces).</summary>
    private BlockSyntax Body(StatementSyntax original, IEnumerable<StatementSyntax>? prefix = null)
    {
        var inner = original is BlockSyntax b ? b.Statements.AsEnumerable() : [original];
        return Block(List<StatementSyntax>([.. prefix ?? [], .. Instrument(inner)]));
    }

    /// <summary>Tr.Set calls for locals/parameters the statement writes and that are definitely assigned afterwards.</summary>
    private IEnumerable<StatementSyntax> SetsAfter(StatementSyntax s)
    {
        DataFlowAnalysis flow;
        try
        {
            flow = model.AnalyzeDataFlow(s)!;
        }
        catch (ArgumentException)
        {
            yield break;
        }
        if (flow is not { Succeeded: true }) yield break;
        foreach (var symbol in flow.WrittenInside.Where(v => flow.DefinitelyAssignedOnExit.Contains(v)).Distinct(SymbolEqualityComparer.Default))
        {
            if (Trackable(symbol) is { } name) yield return Stmt(Call("Set", Str(name), IdentifierName(name)));
        }
    }

    /// <summary>Variables written inside an expression (out vars, patterns) that are definitely assigned where <paramref name="next"/> starts.</summary>
    private List<StatementSyntax> SetsForDeclaredIn(ExpressionSyntax condition, StatementSyntax next)
    {
        var sets = new List<StatementSyntax>();
        try
        {
            var written = model.AnalyzeDataFlow(condition);
            var firstInside = next is BlockSyntax { Statements.Count: > 0 } b ? b.Statements[0] : next;
            if (written is not { Succeeded: true }) return sets;
            var entry = firstInside is BlockSyntax { Statements.Count: 0 } ? null : model.AnalyzeDataFlow(firstInside);
            foreach (var symbol in written.WrittenInside.Distinct(SymbolEqualityComparer.Default))
            {
                if (entry is { Succeeded: true } && entry.DefinitelyAssignedOnEntry.Contains(symbol) && Trackable(symbol) is { } name)
                {
                    sets.Add(Stmt(Call("Set", Str(name), IdentifierName(name))));
                }
            }
        }
        catch (ArgumentException)
        {
        }
        return sets;
    }

    private static string? Trackable(ISymbol symbol) => symbol switch
    {
        ILocalSymbol { IsRef: false, Type.IsRefLikeType: false, IsConst: false } l => l.Name,
        IParameterSymbol { RefKind: RefKind.None, Type.IsRefLikeType: false } p => p.Name,
        _ => null,
    };

    // ------------------------------------------------------------------ control flow

    public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
    {
        var condition = Cond((ExpressionSyntax)Visit(node.Condition)!, node, node.Condition);
        var then = Body(node.Statement, SetsForDeclaredIn(node.Condition, node.Statement));
        ElseClauseSyntax? @else = null;
        if (node.Else is not null)
        {
            @else = node.Else.Statement is IfStatementSyntax elseIf
                ? ElseClause((StatementSyntax)Visit(elseIf)!)
                : ElseClause(Body(node.Else.Statement));
        }
        return node.WithCondition(condition).WithStatement(then).WithElse(@else);
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        // `while (true)` must stay a constant so the compiler still knows the loop never falls through.
        if (IsConstantTrue(node.Condition))
        {
            return node.WithStatement(Body(node.Statement, [Stmt(Call("Cond", BoolLiteral(true), Num(Line(node)), Str("true")))]));
        }
        return node.WithCondition(Cond((ExpressionSyntax)Visit(node.Condition)!, node, node.Condition))
            .WithStatement(Body(node.Statement, SetsForDeclaredIn(node.Condition, node.Statement)));
    }

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
    {
        if (IsConstantTrue(node.Condition))
        {
            return node.WithStatement(Body(node.Statement, [Stmt(Call("Cond", BoolLiteral(true), Num(Line(node.Condition)), Str("true")))]));
        }
        return node.WithStatement(Body(node.Statement))
            .WithCondition(Cond((ExpressionSyntax)Visit(node.Condition)!, node.Condition, node.Condition));
    }

    private bool IsConstantTrue(ExpressionSyntax? condition) =>
        condition is null || model.GetConstantValue(condition) is { HasValue: true, Value: true };

    private static ExpressionSyntax BoolLiteral(bool value) =>
        LiteralExpression(value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        var declaration = node.Declaration is null ? null : (VariableDeclarationSyntax)Visit(node.Declaration)!;
        var initializers = SeparatedList(node.Initializers.Select(i => (ExpressionSyntax)Visit(i)!));
        var incrementors = SeparatedList(node.Incrementors.Select(i => (ExpressionSyntax)Visit(i)!));

        // Loop variables: declared in the for, or assigned by its initializers/incrementors.
        var names = new List<string>();
        if (node.Declaration is not null) names.AddRange(node.Declaration.Variables.Select(v => v.Identifier.Text));
        foreach (var e in node.Initializers.Concat(node.Incrementors))
        {
            foreach (var id in e.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                if (model.GetSymbolInfo(id).Symbol is { } sym && Trackable(sym) is { } n && !names.Contains(n)) names.Add(n);
            }
        }
        ExpressionSyntax vars = Call("Vars", [.. names.Select(NamedValue)]);
        if (IsConstantTrue(node.Condition))
        {
            // for (;;) / for (...; true; ...): keep the condition constant, record the iteration in the body.
            var prefix = new List<StatementSyntax> { Stmt(vars), Stmt(Call("Cond", BoolLiteral(true), Num(Line(node)), Str("true"))) };
            return node.WithDeclaration(declaration).WithInitializers(initializers).WithIncrementors(incrementors)
                .WithStatement(Body(node.Statement, prefix));
        }
        ExpressionSyntax inner = BinaryExpression(SyntaxKind.LogicalAndExpression, vars, ParenthesizedExpression((ExpressionSyntax)Visit(node.Condition!)!));
        var condition = Call("Cond", inner, Num(Line(node)), Str(node.Condition!.ToString()));

        return node.WithDeclaration(declaration).WithInitializers(initializers).WithIncrementors(incrementors)
            .WithCondition(condition).WithStatement(Body(node.Statement));
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        var expression = (ExpressionSyntax)Visit(node.Expression)!;
        var prefix = new List<StatementSyntax>
        {
            Stmt(Call("Step", Num(Line(node)))),
            Stmt(Call("Set", Str(node.Identifier.Text), IdentifierName(node.Identifier.Text))),
        };
        return node.WithExpression(expression).WithStatement(Body(node.Statement, prefix));
    }

    public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        var expression = (ExpressionSyntax)Visit(node.Expression)!;
        var prefix = new List<StatementSyntax> { Stmt(Call("Step", Num(Line(node)))) };
        foreach (var d in node.Variable.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>())
        {
            prefix.Add(Stmt(Call("Set", Str(d.Identifier.Text), IdentifierName(d.Identifier.Text))));
        }
        return node.WithExpression(expression).WithStatement(Body(node.Statement, prefix));
    }

    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
    {
        if (node.Expression is null) return node;
        var expression = (ExpressionSyntax)Visit(node.Expression)!;
        // Keep the declared return type (return null; / return 0; in a uint method), so Ret<T> infers correctly.
        var converted = model.GetTypeInfo(node.Expression).ConvertedType;
        if (converted is not null && converted.TypeKind != TypeKind.Error && !converted.IsAnonymousType && converted.SpecialType != SpecialType.System_Void)
        {
            expression = CastExpression(ParseTypeName(converted.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)), ParenthesizedExpression(expression));
        }
        return node.WithExpression(Call("Ret", expression, Num(Line(node))));
    }

    /// <summary>Lets the tracer's abort pass through user try/catch blocks.</summary>
    public override SyntaxNode? VisitCatchClause(CatchClauseSyntax node)
    {
        var visited = (CatchClauseSyntax)base.VisitCatchClause(node)!;
        ExpressionSyntax notAborting = PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, ParseExpression($"{Tr}.Aborting"));
        ExpressionSyntax filter = visited.Filter is null
            ? notAborting
            : BinaryExpression(SyntaxKind.LogicalAndExpression, notAborting, ParenthesizedExpression(visited.Filter.FilterExpression));
        return visited.WithFilter(CatchFilterClause(filter));
    }

    private ExpressionSyntax Cond(ExpressionSyntax rewritten, SyntaxNode lineNode, ExpressionSyntax original) =>
        Call("Cond", rewritten, Num(Line(lineNode)), Str(original.ToString()));

    // ------------------------------------------------------------------ expressions

    /// <summary>nums[i] (when read) → Tr.Read(nums, i, "nums", "i").</summary>
    public override SyntaxNode? VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var visited = (ElementAccessExpressionSyntax)base.VisitElementAccessExpression(node)!;
        if (node.ArgumentList.Arguments.Count != 1 || IsWritePosition(node)) return visited;
        var type = model.GetTypeInfo(node.Expression).Type;
        var index = node.ArgumentList.Arguments[0];
        if (index.NameColon is not null || !index.RefKindKeyword.IsKind(SyntaxKind.None)) return visited;
        if (model.GetTypeInfo(index.Expression).Type is { } indexType && indexType.Name is "Index" or "Range") return visited;

        string name = node.Expression.ToString();
        ExpressionSyntax indexName = index.Expression is IdentifierNameSyntax id
            ? Str(id.Identifier.Text)
            : LiteralExpression(SyntaxKind.NullLiteralExpression);
        var target = visited.Expression;
        var arg = visited.ArgumentList.Arguments[0].Expression;

        if (type is IArrayTypeSymbol { Rank: 1 } || IsGeneric(type, "List") || type?.SpecialType == SpecialType.System_String)
        {
            return Call("Read", target, arg, Str(name), indexName);
        }
        if (IsGeneric(type, "Dictionary"))
        {
            return Call("ReadKey", target, arg, Str(name));
        }
        return visited;
    }

    /// <summary>seen.ContainsKey(x) → seen.ContainsKey(Tr.Key(seen, x)) so lookups are recorded.</summary>
    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        if (node.Expression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax or ThisExpressionSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } } member) return visited;
        if (!ProbeMethods.Contains(member.Name.Identifier.Text) || node.ArgumentList.Arguments.Count == 0) return visited;
        if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol method) return visited;
        string ns = method.ContainingType.ContainingNamespace.ToDisplayString();
        if (ns != "System.Collections.Generic") return visited;
        var first = visited.ArgumentList.Arguments[0];
        if (!first.RefKindKeyword.IsKind(SyntaxKind.None)) return visited;
        var receiver = ((MemberAccessExpressionSyntax)visited.Expression).Expression;
        var probed = first.WithExpression(Call("Key", receiver, first.Expression));
        return visited.WithArgumentList(visited.ArgumentList.WithArguments(visited.ArgumentList.Arguments.Replace(first, probed)));
    }

    private static bool IsGeneric(ITypeSymbol? type, string name) =>
        type is INamedTypeSymbol { IsGenericType: true } t && t.Name == name &&
        t.ContainingNamespace.ToDisplayString() == "System.Collections.Generic";

    /// <summary>True when the element access is (part of) something being assigned, incremented or passed by ref.</summary>
    private static bool IsWritePosition(ExpressionSyntax node)
    {
        SyntaxNode current = node;
        while (current.Parent is MemberAccessExpressionSyntax ma && ma.Expression == current
               || current.Parent is ElementAccessExpressionSyntax ea && ea.Expression == current
               || current.Parent is ParenthesizedExpressionSyntax)
        {
            current = current.Parent;
        }
        return current.Parent switch
        {
            AssignmentExpressionSyntax a => a.Left == current,
            PrefixUnaryExpressionSyntax p => p.IsKind(SyntaxKind.PreIncrementExpression) || p.IsKind(SyntaxKind.PreDecrementExpression),
            PostfixUnaryExpressionSyntax => true,
            ArgumentSyntax arg => !arg.RefKindKeyword.IsKind(SyntaxKind.None),
            RefExpressionSyntax => true,
            _ => false,
        } || current.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax { Parent: AssignmentExpressionSyntax asg } tuple } && asg.Left == tuple;
    }
}
