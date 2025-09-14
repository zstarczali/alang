// See https://aka.ms/new-console-template for more information


using Antlr4.Runtime;

var input = """
        ; Quote adat
        (print '(1 2 3))
        (print '(a b c))
        (print '())
        (print (quote (x y z)))

        ; Üres lista kifejezésként is
        (print ())            ; -> ()

        ; Logikaiak
        (print (and 1 2 3))
        (print (or 0 0 0))
        (print (not 0))

        ; Aritmetika
        (print (+ 1 2 3))
        (print (- 10 3 2))
        (print (* 2 3 4))
        (print (/ 20 2 2))

        ; Let és Const
        (const PI 314)
        (print PI)
        (let ((x 2) (y 3))
          (print (+ x y))
          (* x y))

        ; Lambda / Defun / While
        (defun inc (n) (+ n 1))
        (print (inc 41))

        (set i 0)
        (while (< i 3)
          (print i)
          (set i (+ i 1))
        )

        (defun fact (n)
          (if (<= n 1)
              1
              (* n (fact (- n 1)))))
        (print (fact 5))

        (print ((lambda (a) (+ a 5)) 7))
        """;


var lex = new LispLexer(new AntlrInputStream(input));
var tokens = new CommonTokenStream(lex);
var parser = new LispParser(tokens);

var tree = parser.prog();
var v = new EvalVisitor();
v.Visit(tree);

Console.ReadKey();
