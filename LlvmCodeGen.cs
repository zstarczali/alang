using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using LLVMSharp.Interop;

public sealed class LlvmCodeGen : LispBaseVisitor<LLVMValueRef>
{
    private readonly LLVMContextRef Ctx;
    private readonly LLVMModuleRef Module;
    private LLVMBuilderRef Builder;

    private readonly LLVMTypeRef I32;
    private readonly LLVMTypeRef I8Ptr;

    private readonly LLVMValueRef Printf;

    private LLVMValueRef CurrentFunction;
    private readonly Stack<Dictionary<string, LLVMValueRef>> localsStack = new();

    private readonly Dictionary<string, LLVMValueRef> functions = new(StringComparer.Ordinal);

    public LlvmCodeGen(string moduleName = "alang")
    {
        Ctx = LLVMContextRef.Create();
        Module = Ctx.CreateModuleWithName(moduleName);
        Builder = Ctx.CreateBuilder();

        I32 = LLVMTypeRef.Int32;
        // ⬇️ régebbi Interop verziókban NINCS CreatePointerType, használjuk a statikus CreatePointer-t
        I8Ptr = LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0);

        // declare i32 @printf(i8*, ...)
        var printfType = LLVMTypeRef.CreateFunction(I32, new[] { I8Ptr }, true);
        Printf = Module.AddFunction("printf", printfType);

        // int main()
        CurrentFunction = AddFunction("main", Array.Empty<LLVMTypeRef>());
        var entry = CurrentFunction.AppendBasicBlock("entry");
        Builder.PositionAtEnd(entry);

        localsStack.Push(new Dictionary<string, LLVMValueRef>(StringComparer.Ordinal));
    }

    public LLVMModuleRef GetModule() => Module;

    private LLVMValueRef AddFunction(string name, LLVMTypeRef[] paramTypes)
    {
        var ft = LLVMTypeRef.CreateFunction(I32, paramTypes, false);
        var fn = Module.AddFunction(name, ft);
        return fn;
    }

    private static LLVMValueRef I(LLVMTypeRef t, int v) => LLVMValueRef.CreateConstInt(t, (ulong)v, true);
    private LLVMValueRef I(int v) => I(I32, v);

    private LLVMValueRef CreateEntryAlloca(string name, LLVMTypeRef type)
    {
        var tmpBuilder = Ctx.CreateBuilder();

        var entry = CurrentFunction.EntryBasicBlock;
        if (entry.Handle == IntPtr.Zero)
            entry = CurrentFunction.AppendBasicBlock("entry");

        var first = entry.FirstInstruction;
        if (first.Handle == IntPtr.Zero)
            tmpBuilder.PositionAtEnd(entry);
        else
            tmpBuilder.PositionBefore(first);

        var alloca = tmpBuilder.BuildAlloca(type, name);
        tmpBuilder.Dispose();
        return alloca;
    }

    private LLVMValueRef GStr(string s) => Builder.BuildGlobalStringPtr(s);

    private bool TryGetLocal(string name, out LLVMValueRef slot)
    {
        foreach (var scope in localsStack)
            if (scope.TryGetValue(name, out slot))
                return true;
        slot = default;
        return false;
    }

    private LLVMValueRef EnsureLocal(string name)
    {
        if (TryGetLocal(name, out var slot)) return slot;
        slot = CreateEntryAlloca(name, I32);
        localsStack.Peek()[name] = slot;
        return slot;
    }

    // === Visitor-ek ===

    public override LLVMValueRef VisitProg(LispParser.ProgContext ctx)
    {
        LLVMValueRef last = I(0);
        foreach (var e in ctx.expr())
            last = Visit(e);

        Builder.BuildRet(I(0)); // main -> 0
        return last;
    }

    public override LLVMValueRef VisitNumber(LispParser.NumberContext ctx)
        => I(int.Parse(ctx.NUMBER().GetText()));

    public override LLVMValueRef VisitStr(LispParser.StrContext ctx)
        => I(0); // stringekhez most csak printnél nyúlunk közvetlenül

    public override LLVMValueRef VisitSymbol(LispParser.SymbolContext ctx)
    {
        var name = ctx.ID().GetText();
        if (!TryGetLocal(name, out var slot))
            throw new Exception($"Ismeretlen változó: {name}");
        return Builder.BuildLoad2(I32, slot, name + "_val");
    }

    public override LLVMValueRef VisitList(LispParser.ListContext ctx)
    {
        // () → most i32 0 (nincs külön listatípus)
        if (ctx.ChildCount == 2) return I(0);

        // (let ...) külön szabály: delegálunk
        if (ctx.letForm() != null) return Visit(ctx.letForm());

        var head = ctx.GetChild(1).GetText();

        // (quote ...) → adat, itt most i32 0
        if (head == "quote") return I(0);

        // (print expr+)
        if (head == "print")
        {
            foreach (var e in ctx.expr())
            {
                if (e.str() != null)
                {
                    var fmt = GStr("%s\n");
                    var s = e.str().STRING().GetText();
                    var inner = s.Length >= 2 ? s[1..^1] : "";
                    var g = GStr(inner);
                    Builder.BuildCall2(Printf.TypeOf, Printf, new[] { fmt, g }, "");
                }
                else
                {
                    var fmt = GStr("%d\n");
                    var v = Visit(e);
                    Builder.BuildCall2(Printf.TypeOf, Printf, new[] { fmt, v }, "");
                }
            }
            return I(0);
        }

        // (set ID expr)
        if (head == "set")
        {
            string name = ctx.ID(0).GetText();
            var val = Visit(ctx.expr(0));
            var slot = EnsureLocal(name);
            Builder.BuildStore(val, slot);
            return val;
        }

        // (if cond then else)
        if (head == "if")
        {
            var cond = EmitCondition(ctx.condition());
            var thenBB = CurrentFunction.AppendBasicBlock("if_then");
            var elseBB = CurrentFunction.AppendBasicBlock("if_else");
            var mergeBB = CurrentFunction.AppendBasicBlock("if_end");

            Builder.BuildCondBr(cond, thenBB, elseBB);

            // then
            Builder.PositionAtEnd(thenBB);
            var thenVal = Visit(ctx.expr(0));
            Builder.BuildBr(mergeBB);
            thenBB = Builder.InsertBlock;

            // else
            Builder.PositionAtEnd(elseBB);
            var elseVal = Visit(ctx.expr(1));
            Builder.BuildBr(mergeBB);
            elseBB = Builder.InsertBlock;

            // merge
            Builder.PositionAtEnd(mergeBB);
            var phi = Builder.BuildPhi(I32, "iftmp");
            phi.AddIncoming(new[] { thenVal }, new[] { thenBB }, 1);
            phi.AddIncoming(new[] { elseVal }, new[] { elseBB }, 1);
            return phi;
        }

        // logikaiak
        if (head == "not")
        {
            var v = Visit(ctx.expr(0));
            var isZero = Builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, v, I(0), "isz");
            return Builder.BuildZExt(isZero, I32, "not");
        }
        if (head == "and")
        {
            LLVMValueRef acc = I(1);
            foreach (var e in ctx.expr())
            {
                var v = Visit(e);
                var nz = Builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, v, I(0), "nz");
                acc = Builder.BuildAnd(acc, Builder.BuildZExt(nz, I32, ""), "and");
            }
            return acc;
        }
        if (head == "or")
        {
            LLVMValueRef acc = I(0);
            foreach (var e in ctx.expr())
            {
                var v = Visit(e);
                var nz = Builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, v, I(0), "nz");
                acc = Builder.BuildOr(acc, Builder.BuildZExt(nz, I32, ""), "or");
            }
            return acc;
        }

        // (while cond body+)
        if (head == "while")
        {
            var condBB = CurrentFunction.AppendBasicBlock("while_cond");
            var bodyBB = CurrentFunction.AppendBasicBlock("while_body");
            var afterBB = CurrentFunction.AppendBasicBlock("while_after");

            Builder.BuildBr(condBB);

            // cond
            Builder.PositionAtEnd(condBB);
            var c = EmitCondition(ctx.condition());
            Builder.BuildCondBr(c, bodyBB, afterBB);

            // body
            Builder.PositionAtEnd(bodyBB);
            foreach (var e in ctx.expr()) Visit(e);
            Builder.BuildBr(condBB);

            // after
            Builder.PositionAtEnd(afterBB);
            return I(0);
        }

        // (defun name (params) body+)
        if (head == "defun")
        {
            string fname = ctx.ID(0).GetText();
            var paramNames = ctx.ID().Skip(1).Select(id => id.GetText()).ToList();
            var paramTypes = Enumerable.Repeat(I32, paramNames.Count).ToArray();

            if (!functions.TryGetValue(fname, out var fn))
            {
                fn = AddFunction(fname, paramTypes);
                functions[fname] = fn;
            }

            var entry = fn.AppendBasicBlock("entry");
            var savedFn = CurrentFunction;
            var savedBuilder = Builder;
            CurrentFunction = fn;
            Builder = Ctx.CreateBuilder();
            Builder.PositionAtEnd(entry);

            localsStack.Push(new Dictionary<string, LLVMValueRef>(StringComparer.Ordinal));
            for (int i = 0; i < paramNames.Count; i++)
            {
                var slot = CreateEntryAlloca(paramNames[i], I32);
                localsStack.Peek()[paramNames[i]] = slot;
                var arg = fn.GetParam((uint)i);
                Builder.BuildStore(arg, slot);
            }

            LLVMValueRef last = I(0);
            foreach (var e in ctx.expr())
                last = Visit(e);
            Builder.BuildRet(last);

            localsStack.Pop();
            Builder.Dispose();
            Builder = savedBuilder;
            CurrentFunction = savedFn;

            return I(0);
        }

        // (lambda ...) – most nincs closure codegen
        if (head == "lambda") return I(0);

        // (+ - * /)
        if (ctx.@operator() != null)
        {
            var op = ctx.@operator().GetText();
            var es = ctx.expr();

            LLVMValueRef acc;
            switch (op)
            {
                case "+":
                    acc = I(0);
                    foreach (var e in es) acc = Builder.BuildAdd(acc, Visit(e), "add");
                    return acc;
                case "*":
                    acc = I(1);
                    foreach (var e in es) acc = Builder.BuildMul(acc, Visit(e), "mul");
                    return acc;
                case "-":
                    if (es.Length == 1) return Builder.BuildNeg(Visit(es[0]), "neg");
                    acc = Visit(es[0]);
                    for (int i = 1; i < es.Length; i++)
                        acc = Builder.BuildSub(acc, Visit(es[i]), "sub");
                    return acc;
                case "/":
                    if (es.Length < 2) throw new Exception("(/ a b ...) legalább 2 operandus");
                    acc = Visit(es[0]);
                    for (int i = 1; i < es.Length; i++)
                        acc = Builder.BuildSDiv(acc, Visit(es[i]), "div");
                    return acc;
                default:
                    throw new Exception($"Ismeretlen operátor: {op}");
            }
        }

        // (ID args...)
        if (ctx.ID() != null && ctx.ID().Length > 0)
        {
            string fname = ctx.ID(0).GetText();

            if (!functions.TryGetValue(fname, out var fn))
            {
                var argc = ctx.expr().Length;
                var paramTypes = Enumerable.Repeat(I32, argc).ToArray();
                fn = AddFunction(fname, paramTypes);
                functions[fname] = fn;
            }

            var args = ctx.expr().Select(Visit).ToArray();
            return Builder.BuildCall2(fn.TypeOf, fn, args, fname + "_call");
        }

        // ((head-expr) args...) – general call (lambda) most nincs
        if (ctx.expr().Length >= 1)
            return I(0);

        throw new Exception($"Ismeretlen listaforma LLVM codegenben: {ctx.GetText()}");
    }

    // condition: '(' compOp expr expr ')'
    private LLVMValueRef EmitCondition(LispParser.ConditionContext c)
    {
        var left = Visit(c.expr(0));
        var right = Visit(c.expr(1));
        var op = c.compOp().GetText();

        return op switch
        {
            "<" => Builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, left, right, "lt"),
            ">" => Builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, left, right, "gt"),
            "=" => Builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, left, right, "eq"),
            "!=" => Builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, left, right, "ne"),
            "<=" => Builder.BuildICmp(LLVMIntPredicate.LLVMIntSLE, left, right, "le"),
            ">=" => Builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, left, right, "ge"),
            _ => throw new Exception($"Ismeretlen összehasonlító: {op}")
        };
    }

    public void WriteOutputs(string llPath = "alang.ll", string bcPath = "alang.bc")
    {
        // Verify nem bool-t ad vissza
        Module.Verify(LLVMVerifierFailureAction.LLVMPrintMessageAction);
        Module.PrintToFile(llPath);
        Module.WriteBitcodeToFile(bcPath);
    }
}
