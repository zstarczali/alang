using System;
using System.Collections.Generic;
using System.Linq;

// ----- ÉRTÉKTÍPUSOK -----
public abstract record Value;
public record IntVal(int V) : Value;
public record StrVal(string V) : Value;
public record SymVal(string Name) : Value;
public record ListVal(List<Value> Items) : Value;
public record FuncVal(
    List<string> Params,
    List<LispParser.ExprContext> Body,
    Dictionary<string, Value> Closure
) : Value;

public class EvalVisitor : LispBaseVisitor<Value>
{
    private readonly Stack<Dictionary<string, Value>> envStack = new();
    private readonly Dictionary<string, FuncVal> funcs = new(StringComparer.Ordinal);
    private readonly HashSet<string> constVars = new(StringComparer.Ordinal);

    public EvalVisitor()
    {
        envStack.Push(new Dictionary<string, Value>(StringComparer.Ordinal));
        envStack.Peek()["nil"] = new ListVal(new List<Value>()); // alap üres lista
    }

    private Dictionary<string, Value> Env => envStack.Peek();

    private bool TryGetVar(string name, out Value value)
    {
        foreach (var scope in envStack)
            if (scope.TryGetValue(name, out value))
                return true;
        value = new IntVal(0);
        return false;
    }

    private void SetVar(string name, Value value)
    {
        if (constVars.Contains(name))
            throw new Exception($"Const változó nem módosítható: {name}");

        foreach (var scope in envStack)
            if (scope.ContainsKey(name)) { scope[name] = value; return; }

        Env[name] = value;
    }

    private Dictionary<string, Value> SnapshotCurrentEnv()
    {
        var snap = new Dictionary<string, Value>(StringComparer.Ordinal);
        foreach (var scope in envStack.Reverse())
            foreach (var kv in scope)
                snap[kv.Key] = kv.Value;
        return snap;
    }

    // Konverziók + render
    private static bool ToBool(Value v) =>
        v switch
        {
            IntVal iv => iv.V != 0,
            ListVal lv => lv.Items.Count != 0,
            StrVal sv => !string.IsNullOrEmpty(sv.V),
            SymVal sm => !string.IsNullOrEmpty(sm.Name),
            FuncVal => true,
            _ => false
        };

    private static int ToInt(Value v) =>
        v switch
        {
            IntVal iv => iv.V,
            StrVal sv => int.TryParse(sv.V, out var n) ? n : throw new Exception($"Nem egész: \"{sv.V}\""),
            _ => throw new Exception("Szám szükséges.")
        };

    private static string Render(Value v) =>
        v switch
        {
            IntVal iv => iv.V.ToString(),
            StrVal sv => sv.V,
            SymVal sm => sm.Name,
            ListVal lv => "(" + string.Join(" ", lv.Items.Select(Render)) + ")",
            FuncVal => "#<function>",
            _ => "<unknown>"
        };

    // ----- TOP -----
    public override Value VisitProg(LispParser.ProgContext ctx)
    {
        Value last = new IntVal(0);
        foreach (var e in ctx.expr())
            last = Visit(e);
        return last;
    }

    public override Value VisitNumber(LispParser.NumberContext ctx)
        => new IntVal(int.Parse(ctx.NUMBER().GetText()));

    public override Value VisitStr(LispParser.StrContext ctx)
    {
        var t = ctx.STRING().GetText();
        return new StrVal(t.Length >= 2 ? t.Substring(1, t.Length - 2) : "");
    }

    public override Value VisitSymbol(LispParser.SymbolContext ctx)
    {
        var name = ctx.ID().GetText();
        if (!TryGetVar(name, out var val))
            throw new Exception($"Ismeretlen változó: {name}");
        return val;
    }

    // ----- QUOTE -----
    public override Value VisitQuoteExpr(LispParser.QuoteExprContext ctx)
        => BuildQuoted(ctx.datum());

    private Value BuildQuoted(LispParser.DatumContext d)
    {
        if (d.number() != null) return VisitNumber(d.number());
        if (d.str() != null) return VisitStr(d.str());
        if (d.symbol() != null) return new SymVal(d.symbol().ID().GetText());
        if (d.dataList() != null) return BuildQuotedList(d.dataList());
        throw new Exception("Ismeretlen datum.");
    }

    private Value BuildQuotedList(LispParser.DataListContext dl)
    {
        var items = new List<Value>();
        foreach (var child in dl.datum())
            items.Add(BuildQuoted(child));
        return new ListVal(items);
    }

    // Általános quote építés expr-ből a hosszú (quote …) formához
    private Value BuildQuotedFromExpr(LispParser.ExprContext e)
    {
        // rövid quote: 'expr  →  (quote expr)
        // Itt biztonságosan felismerjük úgy, hogy az első gyerek egyetlen `'` token.
        if (e.ChildCount >= 2 && e.GetChild(0).GetText() == "'"
            && e.GetChild(1) is LispParser.ExprContext innerQuoted)
        {
            return new ListVal(new List<Value> {
            new SymVal("quote"),
            BuildQuotedFromExpr(innerQuoted)
        });
        }

        // szám, string, szimbólum
        if (e.number() != null) return VisitNumber(e.number());
        if (e.str() != null) return VisitStr(e.str());
        if (e.symbol() != null) return new SymVal(e.symbol().ID().GetText());

        // lista: a benne levő expr-eket rekurzívan, mint ADAT (nem kód) képezzük le
        if (e.list() != null)
        {
            var l = e.list();

            // üres lista kifejezésként: ()
            if (l.ChildCount == 2) // '(' ')'
                return new ListVal(new List<Value>());

            var items = new List<Value>();
            foreach (var sub in l.expr())
                items.Add(BuildQuotedFromExpr(sub));
            return new ListVal(items);
        }

        throw new Exception("Quote-olhatatlan kifejezés.");
    }

    // ----- LET -----
    public override Value VisitLetForm(LispParser.LetFormContext ctx)
    {
        var locals = new Dictionary<string, Value>(StringComparer.Ordinal);
        foreach (var p in ctx.letPair())
        {
            string name = p.ID().GetText();
            var val = Visit(p.expr());
            locals[name] = val;
        }

        envStack.Push(locals);
        Value result = new IntVal(0);
        foreach (var e in ctx.expr())
            result = Visit(e);
        envStack.Pop();
        return result;
    }

    // ----- LIST -----
    public override Value VisitList(LispParser.ListContext ctx)
    {
        // ÜRES LISTA KIFEJEZÉS: ()
        if (ctx.ChildCount == 2)
            return new ListVal(new List<Value>());

        var headText = ctx.ChildCount > 1 ? ctx.GetChild(1).GetText() : "";

        // hosszú quote: (quote expr)
        if (headText == "quote")
        {
            // grammar szerint: '(' 'quote' expr ')'
            var q = ctx.expr(0);
            return BuildQuotedFromExpr(q);
        }

        // print
        if (headText == "print")
        {
            foreach (var e in ctx.expr())
                Console.WriteLine(Render(Visit(e)));
            return new IntVal(0);
        }

        // set
        if (headText == "set")
        {
            string name = ctx.ID(0).GetText();
            var value = Visit(ctx.expr(0));
            SetVar(name, value);
            return value;
        }

        // const
        if (headText == "const")
        {
            string name = ctx.ID(0).GetText();
            if (constVars.Contains(name))
                throw new Exception($"Const változó már definiálva: {name}");
            var value = Visit(ctx.expr(0));
            Env[name] = value;
            constVars.Add(name);
            return value;
        }

        // if
        if (headText == "if")
            return EvalCondition(ctx.condition()) ? Visit(ctx.expr(0)) : Visit(ctx.expr(1));

        // logikaiak
        if (headText == "not")
            return new IntVal(ToBool(Visit(ctx.expr(0))) ? 0 : 1);
        if (headText == "and")
        {
            foreach (var e in ctx.expr())
                if (!ToBool(Visit(e)))
                    return new IntVal(0);
            return new IntVal(1);
        }
        if (headText == "or")
        {
            foreach (var e in ctx.expr())
                if (ToBool(Visit(e)))
                    return new IntVal(1);
            return new IntVal(0);
        }

        // while
        if (headText == "while")
        {
            int guard = 1_000_000;
            while (EvalCondition(ctx.condition()))
            {
                foreach (var e in ctx.expr())
                    Visit(e);
                if (--guard == 0) throw new Exception("Végtelen ciklus?");
            }
            return new IntVal(0);
        }

        // defun
        if (headText == "defun")
        {
            string fname = ctx.ID(0).GetText();
            var paramNames = ctx.ID().Skip(1).Select(t => t.GetText()).ToList();
            var body = ctx.expr().ToList();
            var closure = SnapshotCurrentEnv();
            var fn = new FuncVal(paramNames, body, closure);
            funcs[fname] = fn;
            SetVar(fname, fn);
            return new IntVal(0);
        }

        // lambda
        if (headText == "lambda")
        {
            var paramNames = ctx.ID().Select(t => t.GetText()).ToList();
            var body = ctx.expr().ToList();
            var closure = SnapshotCurrentEnv();
            return new FuncVal(paramNames, body, closure);
        }

        // aritmetika
        if (ctx.@operator() != null)
        {
            string op = ctx.@operator().GetText();
            var es = ctx.expr();

            return op switch
            {
                "+" => new IntVal(es.Select(e => ToInt(Visit(e))).Sum()),
                "*" => new IntVal(es.Select(e => ToInt(Visit(e))).Aggregate(1, (a, b) => a * b)),
                "-" => es.Length == 1
                        ? new IntVal(-ToInt(Visit(es[0])))
                        : new IntVal(es.Skip(1).Select(e => ToInt(Visit(e)))
                                        .Aggregate(ToInt(Visit(es[0])), (a, b) => a - b)),
                "/" => DivChain(es),
                _ => throw new Exception($"Ismeretlen operátor: {op}")
            };
        }

        // név szerinti hívás
        if (ctx.ID() != null && ctx.ID().Length > 0)
        {
            string fname = ctx.ID(0).GetText();
            if (!funcs.TryGetValue(fname, out var fn))
                throw new Exception($"Ismeretlen függvény: {fname}");
            var argVals = ctx.expr().Select(Visit).ToList();
            return Invoke(fn, argVals);
        }

        // általános hívás
        if (ctx.expr().Length >= 1)
        {
            var head = Visit(ctx.expr(0));
            if (head is not FuncVal fv)
                throw new Exception($"Az első elem nem függvény a hívásban: {Render(head)}");
            var args = ctx.expr().Skip(1).Select(Visit).ToList();
            return Invoke(fv, args);
        }

        // Részletesebb hiba
        throw new Exception($"Ismeretlen listaforma: {ctx.GetText()}");
    }

    private Value DivChain(LispParser.ExprContext[] es)
    {
        if (es.Length < 2) throw new Exception("Osztáshoz min. 2 operandus kell.");
        int acc = ToInt(Visit(es[0]));
        for (int i = 1; i < es.Length; i++)
        {
            int d = ToInt(Visit(es[i]));
            if (d == 0) throw new DivideByZeroException("Osztás nullával.");
            acc /= d;
        }
        return new IntVal(acc);
    }

    private Value Invoke(FuncVal fn, List<Value> args)
    {
        if (args.Count != fn.Params.Count)
            throw new Exception($"Hibás arg.szám: {args.Count}, várt {fn.Params.Count}");

        envStack.Push(new Dictionary<string, Value>(fn.Closure, StringComparer.Ordinal));
        var locals = new Dictionary<string, Value>(StringComparer.Ordinal);
        for (int i = 0; i < fn.Params.Count; i++)
            locals[fn.Params[i]] = args[i];
        envStack.Push(locals);

        Value result = new IntVal(0);
        foreach (var e in fn.Body)
            result = Visit(e);

        envStack.Pop(); // locals
        envStack.Pop(); // closure
        return result;
    }

    private bool EvalCondition(LispParser.ConditionContext c)
    {
        string op = c.compOp().GetText();
        int left = ToInt(Visit(c.expr(0)));
        int right = ToInt(Visit(c.expr(1)));

        return op switch
        {
            "<" => left < right,
            ">" => left > right,
            "=" => left == right,
            "!=" => left != right,
            "<=" => left <= right,
            ">=" => left >= right,
            _ => false
        };
    }
}
