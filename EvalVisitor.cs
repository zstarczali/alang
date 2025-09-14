using System;
using System.Collections.Generic;
using System.Linq;

public class EvalVisitor : LispBaseVisitor<int>
{
    // Környezet-verem: lokális scope-ok kezelésére (legfelül az aktuális)
    private readonly Stack<Dictionary<string, int>> envStack = new();

    // Függvények: név -> (paraméterlista, body-expr lista)
    private readonly Dictionary<string, (List<string> Params, List<LispParser.ExprContext> Body)> funcs
        = new(StringComparer.Ordinal);

    bool ToBool(int v) => v != 0;
    int Bool(bool b) => b ? 1 : 0;

    public EvalVisitor()
    {
        // Globális környezet
        envStack.Push(new Dictionary<string, int>(StringComparer.Ordinal));
    }

    private Dictionary<string, int> Env => envStack.Peek();

    public override int VisitProg(LispParser.ProgContext ctx)
    {
        int last = 0;
        foreach (var e in ctx.expr())
            last = Visit(e);
        return last;
    }

    public override int VisitNumber(LispParser.NumberContext ctx)
        => int.Parse(ctx.NUMBER().GetText());

    public override int VisitStr(LispParser.StrContext ctx)
        => 0; // a string értékét jelenleg nem számoljuk, (print) kezeli a kiírást

    private bool TryGetVar(string name, out int value)
    {
        foreach (var scope in envStack)           // top → down
            if (scope.TryGetValue(name, out value))
                return true;
        value = 0;
        return false;
    }

    private void SetVar(string name, int value)
    {
        // ha már létezik bármelyik scope-ban, oda írjuk vissza; különben a legfelsőbe
        foreach (var scope in envStack)
            if (scope.ContainsKey(name)) { scope[name] = value; return; }
        envStack.Peek()[name] = value;
    }

    public override int VisitSymbol(LispParser.SymbolContext ctx)
    {
        var name = ctx.ID().GetText();
        if (!TryGetVar(name, out var v))
            throw new Exception($"Ismeretlen változó: {name}");
        return v;
    }

    public override int VisitList(LispParser.ListContext ctx)
    {
        string head = ctx.GetChild(1).GetText();

        // (print expr+)
        if (head == "print")
        {
            foreach (var e in ctx.expr())
            {
                if (e.str() != null)
                    Console.WriteLine(e.GetText().Trim('"'));
                else
                    Console.WriteLine(Visit(e));
            }
            return 0;
        }

        // (set ID expr)
        if (head == "set")
        {
            string name = ctx.ID(0).GetText();
            int value = Visit(ctx.expr(0));
            SetVar(name, value);
            return value;
        }

        // (while (compOp expr expr) expr+)
        if (head == "while")
        {
            var cond = ctx.condition();
            int guard = 1_000_000; // egyszerű végtelen-ciklus védelem

            while (EvalCondition(cond))
            {
                foreach (var e in ctx.expr())
                    Visit(e);

                if (--guard == 0)
                    throw new Exception("Végtelen ciklus gyanúja (iterációs őr aktiválódott).");
            }
            return 0;
        }

        // (defun name (params...) body+)
        if (head == "defun")
        {
            string fname = ctx.ID(0).GetText();
            // ctx.ID(): [fname, p1, p2, ...]
            var paramNames = ctx.ID().Skip(1).Select(t => t.GetText()).ToList();
            var body = ctx.expr().ToList(); // a defun utáni expr+ a függvény törzse
            funcs[fname] = (paramNames, body);
            return 0;
        }

        if (head == "if")
        {
            var cond = ctx.condition();
            return EvalCondition(cond) ? Visit(ctx.expr(0)) : Visit(ctx.expr(1));
        }

        if (head == "not") return Bool(!ToBool(Visit(ctx.expr(0))));
        if (head == "and") { foreach (var e in ctx.expr()) if (!ToBool(Visit(e))) return 0; return 1; }
        if (head == "or") { foreach (var e in ctx.expr()) if (ToBool(Visit(e))) return 1; return 0; }


        // (+ ...), (* ...), (- ...), (/ ...)
        if (ctx.@operator() != null)
        {
            string op = ctx.@operator().GetText();
            var es = ctx.expr();

            switch (op)
            {
                case "+":
                    {
                        int sum = 0;
                        foreach (var e in es) sum += Visit(e);
                        return sum;
                    }
                case "*":
                    {
                        int prod = 1;
                        foreach (var e in es) prod *= Visit(e);
                        return prod;
                    }
                case "-":
                    {
                        if (es.Length == 1)
                        {
                            // egyoperandusos mínusz (negálás)
                            return -Visit(es[0]);
                        }
                        int acc = Visit(es[0]);
                        for (int i = 1; i < es.Length; i++)
                            acc -= Visit(es[i]);
                        return acc;
                    }
                case "/":
                    {
                        if (es.Length < 2)
                            throw new Exception("Az osztáshoz legalább 2 operandus szükséges: (/ a b ...)");
                        int acc = Visit(es[0]);
                        for (int i = 1; i < es.Length; i++)
                        {
                            int d = Visit(es[i]);
                            if (d == 0) throw new DivideByZeroException("Osztás nullával.");
                            acc /= d; // egész osztás
                        }
                        return acc;
                    }
                default:
                    throw new Exception($"Ismeretlen operátor: {op}");
            }
        }

        // (fname args...)  -- függvényhívás
        if (ctx.ID() != null && ctx.ID().Length > 0)
        {
            string fname = ctx.ID(0).GetText();
            if (!funcs.TryGetValue(fname, out var fn))
                throw new Exception($"Ismeretlen függvény: {fname}");

            var args = ctx.expr().Select(Visit).ToList();
            if (args.Count != fn.Params.Count)
                throw new Exception($"Hibás arg.szám a(z) {fname} hívásban. Várt: {fn.Params.Count}, kapott: {args.Count}");

            // új lokális scope a paraméterekkel
            var locals = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < fn.Params.Count; i++)
                locals[fn.Params[i]] = args[i];

            envStack.Push(locals);
            int result = 0;
            foreach (var bexpr in fn.Body)
                result = Visit(bexpr);
            envStack.Pop();

            return result;
        }

        throw new Exception("Ismeretlen listaforma.");
    }

    private bool EvalCondition(LispParser.ConditionContext c)
    {
        string op = c.compOp().GetText();
        int left = Visit(c.expr(0));
        int right = Visit(c.expr(1));

        return op switch
        {
            "<" => left < right,
            ">" => left > right,
            "=" => left == right,
            "!=" => left != right,
            "<=" => left <= right,
            ">=" => left >= right,
            _ => throw new Exception($"Ismeretlen összehasonlító: {op}")
        };
    }
}
