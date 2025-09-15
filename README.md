# Alang – A Tiny Lisp-Like Language with LLVM Backend

This project is a toy Lisp-like interpreter and compiler written in **C#** using:

- [ANTLR4](https://www.antlr.org/) – parser generator for the Lisp grammar.
- [LLVMSharp.Interop](https://github.com/dotnet/LLVMSharp) – .NET bindings for LLVM.
- .NET 8.0 – runtime and build system.

It has **two execution modes**:

1. **Interpreter (EvalVisitor)** – walks the parse tree and executes the program directly.
2. **LLVM Backend (LlvmCodeGen)** – emits LLVM IR and bitcode, which can be compiled to a native executable with `clang`.

---

## Features

### Interpreter
- Integers, strings, symbols
- Arithmetic: `+ - * /`
- Variables: `set`, `let`, `const`
- Conditionals: `if`
- Loops: `while`
- Logical operators: `and`, `or`, `not`
- Functions: `defun`, recursion
- Anonymous functions: `lambda` (with closures)
- Quoting: `'expr` and `(quote expr)`
- Printing: `print`

### LLVM Backend
- Integers and arithmetic
- Variables: `set`, `let`
- Conditionals: `if`
- Loops: `while`
- Functions: `defun`, recursion
- Printing (`print` for ints and strings)

⚠️ Currently **`lambda` and closures are not yet supported in the LLVM backend**.

---

## Example Program

```lisp
(print "LLVM test")

(set x 0)
(while (< x 3)
  (print x)
  (set x (+ x 1))
)

(defun add (a b)
  (+ a b))
(print (add 10 32))

(let ((y 5) (z 7))
  (print (+ y z))
  (* y z))

(defun fact (n)
  (if (<= n 1)
      1
      (* n (fact (- n 1)))))
(print (fact 5))

;; Lambda + closure works in the interpreter:
(let ((inc (lambda (n) (+ n 1))))
  (print (inc 41)))
```

## Requirements

- .NET 8.0 SDK
- LLVM (native library, matching the LLVMSharp.Interop version)

## Installing LLVM
  `choco install llvm --version=14.0.6`
  
  Then add C:\Program Files\LLVM\bin to your PATH.

## Build & Run

Build and run with .NET:

```bash
dotnet restore
dotnet build
dotnet run
```

This will:
  - Run the interpreter.
  - Emit LLVM IR (alang.ll) and LLVM bitcode (alang.bc).
