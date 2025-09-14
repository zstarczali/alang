grammar Lisp;

prog: expr+ EOF;

expr
    : number
    | str
    | symbol
    | list
    | quoteExpr                  // 'datum
    ;

number : NUMBER ;
str    : STRING ;
symbol : ID ;

// --- quote adatstruktúrák ---
quoteExpr : '\'' datum ;

datum
    : number
    | str
    | symbol
    | dataList
    ;

dataList : '(' datum* ')' ;

// --- listák / utasítások / hívások ---
list
    : '(' operator expr+ ')'                         // (+ ...), (- ...), (* ...), (/ ...)
    | '(' 'print' expr+ ')'                          // (print ...)
    | '(' 'set' ID expr ')'                          // (set x 123)
    | '(' 'if' condition expr expr ')'               // (if (< x 10) then else)
    | '(' 'while' condition expr+ ')'                // (while (< x 10) body...)
    | '(' 'defun' ID '(' ID* ')' expr+ ')'           // (defun name (args...) body...)
    | '(' 'lambda' '(' ID* ')' expr+ ')'             // (lambda (args...) body...)
    | '(' 'and' expr+ ')'                            // (and ...)
    | '(' 'or' expr+ ')'                             // (or ...)
    | '(' 'not' expr ')'                             // (not ...)
    | '(' ID expr* ')'                               // név szerinti hívás
    | '(' expr expr* ')'                             // általános hívás (lambda ...)
    ;

// --- feltétel ---
condition : '(' compOp expr expr ')' ;
compOp   : '<' | '>' | '=' | '!=' | '<=' | '>=' ;

// --- operátorok ---
operator : '+' | '-' | '*' | '/' ;

// --- lexer ---
ID     : [a-zA-Z_][a-zA-Z0-9_]* ;
NUMBER : [0-9]+ ;
STRING : '"' (~["\r\n])* '"' ;

WS      : [ \t\r\n]+ -> skip ;
COMMENT : ';' ~[\r\n]* -> skip ;   // ;-től sor végéig komment
