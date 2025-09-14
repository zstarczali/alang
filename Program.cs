// See https://aka.ms/new-console-template for more information


using Antlr4.Runtime;

var input = """
        (print "Aritmetika teszt")
        (print (+ 1 2 3))        ; 6
        (print (* 2 3 4))        ; 24
        (print (- 10 3 2))       ; 5
        (print (- 7))            ; -7
        (print (/ 20 2 2))       ; 5 (egész osztás)

        (print "While teszt + set")
        (set x 0)
        (while (< x 3)
          (print x)
          (set x (+ x 1))
        )

        (print "Defun teszt")
        (defun inc (a)
          (+ a 1)
        )
        (print (inc 41))         ; 42

        (defun sum3 (a b c)
          (+ (+ a b) c)
        )
        (print (sum3 10 20 7))   ; 37
        ;
        (defun fib (n)
          (if (<= n 1)
              n
              (+ (fib (- n 1)) (fib (- n 2))))
        )
        (print (fib 10))    ; 55
        """;


var lex = new LispLexer(new AntlrInputStream(input));
var tokens = new CommonTokenStream(lex);
var parser = new LispParser(tokens);

var tree = parser.prog();      // belépési pont
var v = new EvalVisitor();
int result = v.Visit(tree);    
// Console.WriteLine(result);

Console.ReadKey();
