// See https://aka.ms/new-console-template for more information


using Antlr4.Runtime;

var input = """
        ; --- quote (’), listák és szimbólumok ---
        (print '(1 2 3))
        (print '(a b c))
        (print '())          ; üres lista
        (print 'hello)       ; szimbólum kiírása

        ; --- logikai műveletek (0 = false, minden más = true) ---
        (print (and 1 2 3))  ; -> 1
        (print (and 1 0 3))  ; -> 0
        (print (or 0 0 5))   ; -> 1
        (print (or 0 0 0))   ; -> 0
        (print (not 0))      ; -> 1
        (print (not 7))      ; -> 0

        ; --- aritmetika ---
        (print (+ 1 2 3))    ; -> 6
        (print (- 10 3 2))   ; -> 5
        (print (- 7))        ; -> -7
        (print (* 2 3 4))    ; -> 24
        (print (/ 20 2 2))   ; -> 5  (egész osztás)

        ; --- while + set példa ---
        (set x 0)
        (while (< x 3)
          (print x)
          (set x (+ x 1))
        )

        ; --- if + defun + rekurzió (faktoriális) ---
        (defun fact (n)
          (if (<= n 1)
              1
              (* n (fact (- n 1)))))
        (print (fact 5))     ; -> 120

        ; --- rekurzív Fibonacci ---
        (defun fib (n)
          (if (<= n 1)
              n
              (+ (fib (- n 1)) (fib (- n 2)))))
        (print (fib 10))     ; -> 55

        ; --- lambda közvetlen hívása ---
        (print ((lambda (y) (+ y 5)) 7))   ; -> 12

        ; --- closure demonstráció beágyazott lambdákkal (változó „befagyasztása”) ---
        ; Itt a külső lambda kap egy 'base' értéket, a belső lambda ezt használja fel.
        (print ((lambda (base) ((lambda (y) (+ y base)) 10)) 5)) ; -> 15
        
        """;


var lex = new LispLexer(new AntlrInputStream(input));
var tokens = new CommonTokenStream(lex);
var parser = new LispParser(tokens);

var tree = parser.prog();
var v = new EvalVisitor();
v.Visit(tree);

Console.ReadKey();
