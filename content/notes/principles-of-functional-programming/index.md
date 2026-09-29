---
title: "Principles of Functional Programming"
description: "The core functional principles and why they matter."
date: 2025-11-02
updatedDate: 2026-09-29
tags: ["functional programming", "F#", "software design"]
---

I spent my early programming years writing Java. It got the job done, but there was a lot of ceremony: class hierarchies, verbose patterns, state scattered across objects. Then a course at DTU, [02157 Functional Programming](https://kurser.dtu.dk/course/02157), put F# in front of me, and the contrast was immediate. I was writing less code, and the compiler caught my mistakes before I could even run the program.

That course changed how I write all code, not just the functional kind. This post walks through the principles that stuck. The examples are in F#, and since I currently build backend systems for mission-critical communications for a living, a few of them borrow that domain. The principles themselves apply in any language.

## Pure Functions

A pure function always returns the same output for the same input and has no side effects.

```fsharp
let add x y = x + y // Always predictable
```

To see why that matters, here's the impure alternative:

```fsharp
let mutable rate = 2.50 // Airtime charged per minute, in DKK

let callCost minutes =
    rate <- rate + 0.25 // Modifies external state
    minutes * rate

callCost 10.0 // 27.5
callCost 10.0 // 30.0, different result, same input
```

Every call bumps `rate`, so the result depends on when and how often you call it. Debugging code like this means reconstructing history. Compare:

```fsharp
let callCost rate minutes = minutes * rate

callCost 2.50 10.0 // 25.0
callCost 2.50 10.0 // 25.0, always the same
```

The pure version takes everything it needs as arguments. Testing it requires no setup: pass values in, check what comes out.

A useful test for purity: if you can replace a function call with its result and nothing changes, the function is pure. This property is called *referential transparency*.

```fsharp
let square x = x * x
let result = square 5 + square 5
// You can swap in 25 + 25 and the program behaves identically
```

Referential transparency is what makes pure functions safe to refactor and easy to compose.

## Immutability

Instead of changing existing values, you create new ones.

```fsharp
let numbers = [1; 2; 3]
let newNumbers = 0 :: numbers // [0; 1; 2; 3], original list unchanged
```

This sounds wasteful, but functional data structures are *persistent*: they share structure with previous versions instead of copying. Prepending `0` above allocates a single node; the new list reuses `[1; 2; 3]` as its tail. Immutable operations are usually cheaper than people expect.

The payoff is that data can't change out from under you, which removes a whole class of bugs — especially in concurrent code, where shared mutable state causes most of the misery.

To be clear, I still write `let mutable` inside a function when a local loop is the clearest way to express something. Contained mutation is an implementation detail. It's *shared* mutation that hurts.

## Expressions, Not Statements

In most imperative languages, `if` blocks and loops are *statements*: they do something but produce no value. In functional programming, almost everything is an *expression* that evaluates to a value.

```fsharp
let score = 72
let status = if score >= 50 then "Pass" else "Fail"
// status = "Pass"
```

There's no mutable variable that gets assigned inside each branch. The `if/else` itself produces the value. Same for `match`:

```fsharp
let level = 2
let description =
    match level with
    | 1 -> "Beginner"
    | 2 -> "Intermediate"
    | _ -> "Advanced"
// description = "Intermediate"
```

The practical consequence: you can't leave a variable uninitialized, and you can't forget to assign it in one branch, because the expression either produces a value or it doesn't compile. Expressions also nest and compose, which everything below builds on.

## First-Class and Higher-Order Functions

In functional programming, functions are values. You can assign them to variables, pass them as arguments, and return them from other functions.

```fsharp
let add x y = x + y
let sum = add // Assign a function to a variable
sum 2 3 // 5

// Return a function from a function
let multiplier factor =
    let multiply x = x * factor
    multiply

let triple = multiplier 3
triple 5 // 15
```

Notice how `multiply` still has access to `factor` after `multiplier` returns. This is a *closure*: the inner function captures variables from its enclosing scope.

Once functions are values, you can write *higher-order functions*, functions that take other functions as arguments:

```fsharp
let doubled = List.map (fun x -> x * 2) [1; 2; 3] // [2; 4; 6]
let evens = List.filter (fun x -> x % 2 = 0) [1; 2; 3; 4] // [2; 4]
```

`List.map` and `List.filter` are the workhorses of functional code. Instead of writing loops to transform data, you describe the transformation.

## Currying and Partial Application

In F#, every multi-parameter function is actually a chain of single-parameter functions. This is called *currying*. It means you can supply some arguments now and get back a function waiting for the rest.

```fsharp
let add x y = x + y // Curried by default
let add5 = add 5 // Partial application, returns a new function
add5 10 // 15

// Create a specialized function, then map it over a list
let numbers = [1; 2; 3; 4]
let add10 = add 10
List.map add10 numbers // [11; 12; 13; 14]
```

Partial application lets you build specialized functions from general ones without any boilerplate.

## Function Composition

Rather than nesting function calls or writing step-by-step procedures, you can compose small functions into pipelines.

```fsharp
let double x = x * 2
let increment x = x + 1

let incrementThenDouble = increment >> double
incrementThenDouble 3 // 8
```

The `>>` operator feeds the output of the first function into the second, so the code states *what* transforms happen rather than the mechanics of passing values along.

## Recursion

Functional programming favors recursion over imperative loops, since traditional loops rely on a mutable counter, which clashes with immutability.

```fsharp
let rec factorial n =
    if n <= 1 then 1
    else n * factorial (n - 1)

factorial 5 // 120

// Tail-recursive version (no stack growth)
let factorialTail n =
    let rec loop acc n =
        if n <= 1 then acc
        else loop (acc * n) (n - 1)
    loop 1 n
```

Every recursive function needs a base case (here, `if n <= 1`) to terminate. The tail-recursive version restructures the computation so the recursive call is the last thing the function does, letting the compiler turn it into a loop under the hood.

That said, explicit recursion isn't your everyday tool. Higher-order functions like `List.map`, `List.filter`, and `List.fold` handle most iteration more clearly. Recursion is the mechanism they're built on, but those abstractions are what you'll actually reach for.

## Declarative Thinking

Functional programming nudges you toward saying *what* you want rather than spelling out *how* to get there. Say you need the total airtime across completed calls:

```fsharp
type Call = { Minutes: float; Completed: bool }

let calls =
    [ { Minutes = 12.5; Completed = true }
      { Minutes = 3.2; Completed = false }
      { Minutes = 20.1; Completed = true } ]
```

An imperative approach:

```fsharp
let mutable total = 0.0
for call in calls do
    if call.Completed then
        total <- total + call.Minutes
// total = 32.6
```

It works, but the intent is buried in mechanics. F# has a pipe operator (`|>`) that passes the result of one expression to the next function, so the same logic reads top to bottom:

```fsharp
let total =
    calls
    |> List.filter (fun c -> c.Completed)
    |> List.sumBy (fun c -> c.Minutes)
// 32.6
```

Take the calls, keep the completed ones, sum the airtime. Each step is explicit and independently testable.

Step back and a pattern emerges across all of these ideas: FP programs are pipelines of small, composable transformations over immutable data. Pure functions make each step predictable, immutability guarantees the data holds still between steps, and composition connects them. What remains is modeling the data itself.

## Algebraic Data Types and Pattern Matching

Functional languages give you tools to model data precisely. The simplest example is `Option`, which represents the presence or absence of a value explicitly instead of hiding it behind `null`.

```fsharp
let radios = [ "RAD-001"; "RAD-002"; "RAD-007" ]

let found = radios |> List.tryFind (fun id -> id = "RAD-007")
// found : string option = Some "RAD-007"

let missing = radios |> List.tryFind (fun id -> id = "RAD-042")
// missing : string option = None
```

There's no `NullReferenceException` waiting to happen. The type says the value might be absent, so you have to unwrap it before you can use it as a `string`, and unwrapping it with a `match` means handling both cases.

`Option` is a *discriminated union*: a type with named cases. You can define your own to model domain state. A radio on the network, for instance, is always in exactly one of a few states:

```fsharp
type RadioState =
    | Available
    | InCall of callId: string
    | Faulted of errorCode: int

let describe state =
    match state with
    | Available -> "Ready for calls"
    | InCall callId -> $"Call {callId} in progress"
    | Faulted code -> $"Out of service (error {code})"
```

The `match` is exhaustive: add an `Emergency` case next sprint and the compiler warns about every `match` that doesn't handle it. And there's no combination of boolean flags that means nothing, no "in a call but also available" state. The business rule lives in the type, and the compiler enforces it.

## Explicit Error Handling

Instead of throwing exceptions, functional code tends to return types like `Result` that make failure part of the signature.

```fsharp
let divide x y =
    if y = 0 then Error "Division by zero"
    else Ok (x / y)

match divide 10 2 with
| Ok result -> printfn "Result: %d" result
| Error msg -> printfn "Error: %s" msg
```

The caller can't get at the result without deciding what happens on failure; the error case is right there in the type. Where this pays off is composition. Real code chains operations that can each fail, and without explicit errors you end up with nested `try/catch` or pyramids of `if`. With `Result`, you chain steps using `Result.bind`, and the first failure short-circuits the rest:

```fsharp
let validateDuration minutes =
    if minutes > 0.0 then Ok minutes
    else Error "Duration must be positive"

let validateLimit minutes =
    if minutes <= 150.0 then Ok minutes
    else Error "Exceeds the maximum call duration"

12.5  |> validateDuration |> Result.bind validateLimit // Ok 12.5
-1.0  |> validateDuration |> Result.bind validateLimit // Error "Duration must be positive"
200.0 |> validateDuration |> Result.bind validateLimit // Error "Exceeds the maximum call duration"
```

Each validation is a small function you can test on its own, and the pipeline carries the first error forward.

## Separation of Effects

Keep your core logic pure and push side effects to the edges of your program. Business rules stay deterministic and testable; I/O, state mutations, and network calls live in a thin outer shell.

```fsharp
// Pure core logic
let callCost rate minutes = minutes * rate

// Impure shell (I/O at the edges)
let finalizeCall callId =
    let call = loadCall callId // Side effect
    let cost = callCost call.Rate call.Minutes // Pure
    saveCallRecord callId cost // Side effect
```

The pure `callCost` is trivial to test: pass in numbers, check the output. The impure `finalizeCall` is a thin wrapper that orchestrates I/O around it. When something breaks, you know which half to suspect.

## Why These Principles Matter

Pure functions and immutability remove the bugs that come from hidden state. Testing stops requiring elaborate setup. Refactoring gets safer because each function can be understood on its own. And when you come back to the code months later, there are no invisible side effects to re-learn.

Looking back, switching from Java to F# mattered less than what the switch taught me: favor data over objects, transformations over mutation, types over runtime checks. Most of my day-to-day work today is Elixir, where half of that comes built in: data is immutable, and pattern matching and `|>` pipelines are simply how you write it. The other half doesn't — the compiler won't promise that a `case` covers every shape, and nothing stops a function deep in the core from doing I/O. That half I still carry over by hand. The principles travel; sometimes the language meets them halfway.
