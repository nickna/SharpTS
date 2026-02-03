using SharpTS.Parsing;

namespace SharpTS.TypeSystem.ControlFlow;

/// <summary>
/// Builds a control flow graph from a list of statements.
/// </summary>
public sealed class ControlFlowBuilder
{
    private readonly List<BasicBlock> _blocks = [];
    private readonly List<FlowEdge> _edges = [];
    private BasicBlock _currentBlock;
    private readonly BasicBlock _entry;
    private readonly BasicBlock _exit;

    // For break/continue handling
    private readonly Stack<BasicBlock> _breakTargets = new();
    private readonly Stack<BasicBlock> _continueTargets = new();

    // For labeled statements
    private readonly Dictionary<string, BasicBlock> _labeledBreakTargets = new();
    private readonly Dictionary<string, BasicBlock> _labeledContinueTargets = new();

    private ControlFlowBuilder()
    {
        _entry = CreateBlock("entry");
        _exit = CreateBlock("exit");
        _currentBlock = _entry;
    }

    /// <summary>
    /// Builds a CFG from a list of statements.
    /// </summary>
    public static ControlFlowGraph Build(List<Stmt> statements)
    {
        var builder = new ControlFlowBuilder();
        builder.ProcessStatements(statements);

        // Connect any remaining flow to exit
        if (!builder._currentBlock.IsTerminating && builder._currentBlock != builder._exit)
        {
            builder.AddEdge(builder._currentBlock, builder._exit, FlowEdgeKind.Unconditional);
        }

        return new ControlFlowGraph(
            builder._entry,
            builder._exit,
            builder._blocks,
            builder._edges);
    }

    private BasicBlock CreateBlock(string? label = null)
    {
        var block = new BasicBlock(label);
        _blocks.Add(block);
        return block;
    }

    private void AddEdge(BasicBlock from, BasicBlock to, FlowEdgeKind kind, Expr? condition = null, bool conditionIsTrue = true)
    {
        var edge = new FlowEdge(from, to, kind, condition, conditionIsTrue);
        _edges.Add(edge);
        from.Successors.Add(edge);
        to.Predecessors.Add(edge);
    }

    private void StartNewBlock(string? label = null)
    {
        var newBlock = CreateBlock(label);
        if (!_currentBlock.IsTerminating)
        {
            AddEdge(_currentBlock, newBlock, FlowEdgeKind.Unconditional);
        }
        _currentBlock = newBlock;
    }

    private void ProcessStatements(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            ProcessStatement(stmt);
        }
    }

    private void ProcessStatement(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Block block:
                ProcessStatements(block.Statements);
                break;

            case Stmt.Sequence seq:
                ProcessStatements(seq.Statements);
                break;

            case Stmt.If ifStmt:
                ProcessIf(ifStmt);
                break;

            case Stmt.While whileStmt:
                ProcessWhile(whileStmt);
                break;

            case Stmt.DoWhile doWhileStmt:
                ProcessDoWhile(doWhileStmt);
                break;

            case Stmt.For forStmt:
                ProcessFor(forStmt);
                break;

            case Stmt.ForOf forOfStmt:
                ProcessForOf(forOfStmt);
                break;

            case Stmt.ForIn forInStmt:
                ProcessForIn(forInStmt);
                break;

            case Stmt.Switch switchStmt:
                ProcessSwitch(switchStmt);
                break;

            case Stmt.TryCatch tryCatchStmt:
                ProcessTryCatch(tryCatchStmt);
                break;

            case Stmt.Return returnStmt:
                _currentBlock.AddStatement(returnStmt);
                AddEdge(_currentBlock, _exit, FlowEdgeKind.Return);
                StartNewBlock("after-return");
                break;

            case Stmt.Throw throwStmt:
                _currentBlock.AddStatement(throwStmt);
                // For simplicity, throw goes to exit (proper exception handling would need more)
                AddEdge(_currentBlock, _exit, FlowEdgeKind.Throw);
                StartNewBlock("after-throw");
                break;

            case Stmt.Break breakStmt:
                _currentBlock.AddStatement(breakStmt);
                var breakTarget = breakStmt.Label != null
                    ? _labeledBreakTargets.GetValueOrDefault(breakStmt.Label.Lexeme)
                    : _breakTargets.Count > 0 ? _breakTargets.Peek() : null;

                if (breakTarget != null)
                {
                    AddEdge(_currentBlock, breakTarget, FlowEdgeKind.Break);
                }
                StartNewBlock("after-break");
                break;

            case Stmt.Continue continueStmt:
                _currentBlock.AddStatement(continueStmt);
                var continueTarget = continueStmt.Label != null
                    ? _labeledContinueTargets.GetValueOrDefault(continueStmt.Label.Lexeme)
                    : _continueTargets.Count > 0 ? _continueTargets.Peek() : null;

                if (continueTarget != null)
                {
                    AddEdge(_currentBlock, continueTarget, FlowEdgeKind.Continue);
                }
                StartNewBlock("after-continue");
                break;

            case Stmt.LabeledStatement labeled:
                ProcessLabeledStatement(labeled);
                break;

            default:
                // All other statements are added to the current block
                _currentBlock.AddStatement(stmt);
                break;
        }
    }

    private void ProcessIf(Stmt.If ifStmt)
    {
        // Add condition check to current block (implicitly)
        var conditionBlock = _currentBlock;

        // Create blocks
        var thenBlock = CreateBlock("then");
        var mergeBlock = CreateBlock("if-merge");
        var elseBlock = ifStmt.ElseBranch != null ? CreateBlock("else") : mergeBlock;

        // Add conditional edges
        AddEdge(conditionBlock, thenBlock, FlowEdgeKind.ConditionalTrue, ifStmt.Condition, true);
        AddEdge(conditionBlock, elseBlock, FlowEdgeKind.ConditionalFalse, ifStmt.Condition, false);

        // Process then branch
        _currentBlock = thenBlock;
        ProcessStatement(ifStmt.ThenBranch);
        if (!_currentBlock.IsTerminating)
        {
            AddEdge(_currentBlock, mergeBlock, FlowEdgeKind.Unconditional);
        }

        // Process else branch
        if (ifStmt.ElseBranch != null)
        {
            _currentBlock = elseBlock;
            ProcessStatement(ifStmt.ElseBranch);
            if (!_currentBlock.IsTerminating)
            {
                AddEdge(_currentBlock, mergeBlock, FlowEdgeKind.Unconditional);
            }
        }

        _currentBlock = mergeBlock;
    }

    private void ProcessWhile(Stmt.While whileStmt)
    {
        var headerBlock = CreateBlock("while-header");
        var bodyBlock = CreateBlock("while-body");
        var exitBlock = CreateBlock("while-exit");

        // Flow to header
        AddEdge(_currentBlock, headerBlock, FlowEdgeKind.Unconditional);

        // Conditional edges from header
        AddEdge(headerBlock, bodyBlock, FlowEdgeKind.ConditionalTrue, whileStmt.Condition, true);
        AddEdge(headerBlock, exitBlock, FlowEdgeKind.ConditionalFalse, whileStmt.Condition, false);

        // Process body with break/continue targets
        _breakTargets.Push(exitBlock);
        _continueTargets.Push(headerBlock);

        _currentBlock = bodyBlock;
        ProcessStatement(whileStmt.Body);

        // Back edge to header
        if (!_currentBlock.IsTerminating)
        {
            AddEdge(_currentBlock, headerBlock, FlowEdgeKind.LoopBack);
        }

        _breakTargets.Pop();
        _continueTargets.Pop();

        _currentBlock = exitBlock;
    }

    private void ProcessDoWhile(Stmt.DoWhile doWhileStmt)
    {
        var bodyBlock = CreateBlock("do-body");
        var conditionBlock = CreateBlock("do-condition");
        var exitBlock = CreateBlock("do-exit");

        // Flow to body
        AddEdge(_currentBlock, bodyBlock, FlowEdgeKind.Unconditional);

        // Process body with break/continue targets
        _breakTargets.Push(exitBlock);
        _continueTargets.Push(conditionBlock);

        _currentBlock = bodyBlock;
        ProcessStatement(doWhileStmt.Body);

        if (!_currentBlock.IsTerminating)
        {
            AddEdge(_currentBlock, conditionBlock, FlowEdgeKind.Unconditional);
        }

        _breakTargets.Pop();
        _continueTargets.Pop();

        // Condition block with back edge
        AddEdge(conditionBlock, bodyBlock, FlowEdgeKind.LoopBack, doWhileStmt.Condition, true);
        AddEdge(conditionBlock, exitBlock, FlowEdgeKind.ConditionalFalse, doWhileStmt.Condition, false);

        _currentBlock = exitBlock;
    }

    private void ProcessFor(Stmt.For forStmt)
    {
        // Process initializer in current block
        if (forStmt.Initializer != null)
        {
            ProcessStatement(forStmt.Initializer);
        }

        var headerBlock = CreateBlock("for-header");
        var bodyBlock = CreateBlock("for-body");
        var incrementBlock = CreateBlock("for-increment");
        var exitBlock = CreateBlock("for-exit");

        // Flow to header
        AddEdge(_currentBlock, headerBlock, FlowEdgeKind.Unconditional);

        // Conditional edges from header (if there's a condition)
        if (forStmt.Condition != null)
        {
            AddEdge(headerBlock, bodyBlock, FlowEdgeKind.ConditionalTrue, forStmt.Condition, true);
            AddEdge(headerBlock, exitBlock, FlowEdgeKind.ConditionalFalse, forStmt.Condition, false);
        }
        else
        {
            AddEdge(headerBlock, bodyBlock, FlowEdgeKind.Unconditional);
        }

        // Process body with break/continue targets
        _breakTargets.Push(exitBlock);
        _continueTargets.Push(incrementBlock);

        _currentBlock = bodyBlock;
        ProcessStatement(forStmt.Body);

        if (!_currentBlock.IsTerminating)
        {
            AddEdge(_currentBlock, incrementBlock, FlowEdgeKind.Unconditional);
        }

        _breakTargets.Pop();
        _continueTargets.Pop();

        // Increment block (if there's an increment expression)
        if (forStmt.Increment != null)
        {
            incrementBlock.AddStatement(new Stmt.Expression(forStmt.Increment));
        }
        AddEdge(incrementBlock, headerBlock, FlowEdgeKind.LoopBack);

        _currentBlock = exitBlock;
    }

    private void ProcessForOf(Stmt.ForOf forOfStmt)
    {
        var headerBlock = CreateBlock("forof-header");
        var bodyBlock = CreateBlock("forof-body");
        var exitBlock = CreateBlock("forof-exit");

        AddEdge(_currentBlock, headerBlock, FlowEdgeKind.Unconditional);

        // For-of has implicit "has next element" condition
        AddEdge(headerBlock, bodyBlock, FlowEdgeKind.ConditionalTrue);
        AddEdge(headerBlock, exitBlock, FlowEdgeKind.ConditionalFalse);

        _breakTargets.Push(exitBlock);
        _continueTargets.Push(headerBlock);

        _currentBlock = bodyBlock;
        ProcessStatement(forOfStmt.Body);

        if (!_currentBlock.IsTerminating)
        {
            AddEdge(_currentBlock, headerBlock, FlowEdgeKind.LoopBack);
        }

        _breakTargets.Pop();
        _continueTargets.Pop();

        _currentBlock = exitBlock;
    }

    private void ProcessForIn(Stmt.ForIn forInStmt)
    {
        var headerBlock = CreateBlock("forin-header");
        var bodyBlock = CreateBlock("forin-body");
        var exitBlock = CreateBlock("forin-exit");

        AddEdge(_currentBlock, headerBlock, FlowEdgeKind.Unconditional);

        AddEdge(headerBlock, bodyBlock, FlowEdgeKind.ConditionalTrue);
        AddEdge(headerBlock, exitBlock, FlowEdgeKind.ConditionalFalse);

        _breakTargets.Push(exitBlock);
        _continueTargets.Push(headerBlock);

        _currentBlock = bodyBlock;
        ProcessStatement(forInStmt.Body);

        if (!_currentBlock.IsTerminating)
        {
            AddEdge(_currentBlock, headerBlock, FlowEdgeKind.LoopBack);
        }

        _breakTargets.Pop();
        _continueTargets.Pop();

        _currentBlock = exitBlock;
    }

    private void ProcessSwitch(Stmt.Switch switchStmt)
    {
        var exitBlock = CreateBlock("switch-exit");
        _breakTargets.Push(exitBlock);

        BasicBlock? previousCaseBlock = null;
        bool hadDefault = false;

        foreach (var caseStmt in switchStmt.Cases)
        {
            var caseBlock = CreateBlock(caseStmt.Value != null ? "case" : "default");

            if (caseStmt.Value == null)
            {
                hadDefault = true;
            }

            // Fall-through from previous case
            if (previousCaseBlock != null && !previousCaseBlock.IsTerminating)
            {
                AddEdge(previousCaseBlock, caseBlock, FlowEdgeKind.Unconditional);
            }

            // Jump from switch header to case
            AddEdge(_currentBlock, caseBlock, FlowEdgeKind.ConditionalTrue, caseStmt.Value);

            var savedCurrent = _currentBlock;
            _currentBlock = caseBlock;

            foreach (var stmt in caseStmt.Body)
            {
                ProcessStatement(stmt);
            }

            previousCaseBlock = _currentBlock;
            _currentBlock = savedCurrent;
        }

        // Last case flows to exit if not terminated
        if (previousCaseBlock != null && !previousCaseBlock.IsTerminating)
        {
            AddEdge(previousCaseBlock, exitBlock, FlowEdgeKind.Unconditional);
        }

        // If no default case, switch header can skip to exit
        if (!hadDefault)
        {
            AddEdge(_currentBlock, exitBlock, FlowEdgeKind.Unconditional);
        }

        _breakTargets.Pop();
        _currentBlock = exitBlock;
    }

    private void ProcessTryCatch(Stmt.TryCatch tryCatchStmt)
    {
        var tryBlock = CreateBlock("try");
        var catchBlock = tryCatchStmt.CatchBlock != null ? CreateBlock("catch") : null;
        var finallyBlock = tryCatchStmt.FinallyBlock != null ? CreateBlock("finally") : null;
        var exitBlock = CreateBlock("try-exit");

        // Enter try
        AddEdge(_currentBlock, tryBlock, FlowEdgeKind.Unconditional);

        // Process try block
        _currentBlock = tryBlock;
        ProcessStatements(tryCatchStmt.TryBlock);

        // Flow from try to finally or exit
        var afterTry = finallyBlock ?? exitBlock;
        if (!_currentBlock.IsTerminating)
        {
            AddEdge(_currentBlock, afterTry, FlowEdgeKind.Unconditional);
        }

        // Implicit edge from try to catch (for exceptions)
        if (catchBlock != null)
        {
            AddEdge(tryBlock, catchBlock, FlowEdgeKind.Throw);

            _currentBlock = catchBlock;
            ProcessStatements(tryCatchStmt.CatchBlock!);

            var afterCatch = finallyBlock ?? exitBlock;
            if (!_currentBlock.IsTerminating)
            {
                AddEdge(_currentBlock, afterCatch, FlowEdgeKind.Unconditional);
            }
        }

        // Process finally
        if (finallyBlock != null)
        {
            _currentBlock = finallyBlock;
            ProcessStatements(tryCatchStmt.FinallyBlock!);

            if (!_currentBlock.IsTerminating)
            {
                AddEdge(_currentBlock, exitBlock, FlowEdgeKind.Unconditional);
            }
        }

        _currentBlock = exitBlock;
    }

    private void ProcessLabeledStatement(Stmt.LabeledStatement labeled)
    {
        var labelName = labeled.Label.Lexeme;
        var exitBlock = CreateBlock($"label-{labelName}-exit");

        // For loops, the label applies to break/continue
        bool isLoop = labeled.Statement is Stmt.While or Stmt.DoWhile or Stmt.For or Stmt.ForOf or Stmt.ForIn;

        _labeledBreakTargets[labelName] = exitBlock;

        if (isLoop)
        {
            // For loops, we need to track continue target which is set during loop processing
            // This is handled by the loop processing pushing to continue targets
        }

        try
        {
            ProcessStatement(labeled.Statement);

            if (!_currentBlock.IsTerminating)
            {
                AddEdge(_currentBlock, exitBlock, FlowEdgeKind.Unconditional);
            }

            _currentBlock = exitBlock;
        }
        finally
        {
            _labeledBreakTargets.Remove(labelName);
            _labeledContinueTargets.Remove(labelName);
        }
    }
}
