grammar Lisp;

prog: expr+ EOF;

expr
    : number
    | str
    | symbol
    | list
    ;

number : NUMBER ;
str    : STRING ;
symbol : ID ;

list
    : '(' operator expr+ ')'              // (+ ...), (* ...), (- ...), (/ ...)
    | '(' 'print' expr+ ')'               // (print ...)
    | '(' 'set' ID expr ')'               // (set x 123)
    | '(' 'while' condition expr+ ')'     // (while (< x 10) (set x (+ x 1)) ...)
    | '(' 'if' condition expr expr ')'       // <-- ÚJ
    | '(' 'defun' ID '(' ID* ')' expr+ ')'// (defun fname (a b) ...body...)
    | '(' 'not' expr ')'
    | '(' 'and' expr+ ')'
    | '(' 'or'  expr+ ')'
    | '(' ID expr* ')'                    // (fname arg1 arg2 ...)
    ;

condition : '(' compOp expr expr ')' ;    // pl. (< x 10)

compOp   : '<' | '>' | '=' | '!=' | '<=' | '>=' ;
operator : '+' | '*' | '-' | '/' ;

ID     : [a-zA-Z_][a-zA-Z0-9_]* ;
NUMBER : [0-9]+ ;
STRING : '"' (~["\r\n])* '"' ;
WS     : [ \t\r\n]+ -> skip ;
COMMENT : ';' ~[\r\n]* -> skip ;
