// See https://aka.ms/new-console-template for more information


using Antlr4.Runtime;

var input = """
        ; --- const ---
        (const PI 314)
        (print PI)              ; 314

        ; --- let ---
        (let ((x 2) (y 3))
          (print (+ x y))       ; 5
          (* x y))              ; 6

        ; --- quote ---
        (print '(1 2 3))
        (print '(a b c))
        (print '())
        (print 'hello)

        ; --- logikaiak ---
        (print (and 1 2 3))     ; 1
        (print (and 1 0 3))     ; 0
        (print (or 0 0 5))      ; 1
        (print (or 0 0 0))      ; 0
        (print (not 0))         ; 1
        (print (not 7))         ; 0

        ; --- aritmetika ---
        (print (+ 1 2 3))       ; 6
        (print (- 10 3 2))      ; 5
        (print (- 7))           ; -7
        (print (* 2 3 4))       ; 24
        (print (/ 20 2 2))      ; 5

        ; --- while + set ---
        (set x 0)
        (while (< x 3)
          (print x)
          (set x (+ x 1))
        )

        ; --- defun + lambda + rekurzió ---
        (defun fact (n)
          (if (<= n 1)
              1
              (* n (fact (- n 1)))))
        (print (fact 5))        ; 120

        (print ((lambda (y) (+ y 5)) 7))  ; 12
        """;


var lex = new LispLexer(new AntlrInputStream(input));
var tokens = new CommonTokenStream(lex);
var parser = new LispParser(tokens);

var tree = parser.prog();
var v = new EvalVisitor();
v.Visit(tree);

Console.ReadKey();
