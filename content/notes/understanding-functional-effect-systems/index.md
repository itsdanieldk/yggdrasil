---
title: "Understanding Functional Effect Systems"
description: "What functional effect systems are, why they matter, and how they make side effects composable."
date: 2026-04-22
updatedDate: 2026-09-29
tags: ["functional programming", "F#", "effect system"]
---

[My previous post on functional programming principles](/notes/principles-of-functional-programming) closed its list of principles with "Separation of Effects": keep your pure logic pure and push side effects to the edges. It's a solid principle, but it depends entirely on discipline. Nothing in the type system stops you from calling a database in the middle of your business logic. You just have to trust yourself, and your team, not to do that.

Effect systems take that discipline and make it structural. Instead of performing side effects directly, you describe them as values, and hand execution to a runtime you control.

## Effects as Values

In most programs, side effects happen immediately. You call `Console.ReadLine()` and it blocks, waiting for input. You call `saveToDatabase(record)` and the write happens on the spot.

In an effect system, you don't perform effects. You describe them. A value of type `IO<string>` doesn't contain a string; it's a description of a computation that, when eventually run, will produce one.

Here's the concept in F# using [FIO](/projects/fio):

```fsharp
// Imperative: performs I/O right now
let name = System.Console.ReadLine()
printfn $"Hello, {name}"

// FIO: describes I/O without performing it
let greet = fio {
    let! name = Console.readLine id // id is the identity function
    do! Console.printLine $"Hello, {name}" id
}
// greet : FIO<unit, exn>, nothing has happened yet
```

The same idea in Scala with ZIO:

```scala
// Imperative
val name = scala.io.StdIn.readLine()
println(s"Hello, $name")

// ZIO: a description of the same program
val greet = for {
  name <- Console.readLine
  _    <- Console.printLine(s"Hello, $name")
} yield ()
// greet is a value, nothing has happened yet
```

Because effects are values, you can pass them around, store them in data structures, and transform them without triggering anything. The program is just data until you decide to run it.

## Composition

This "effects as data" approach might seem like unnecessary indirection, but it's what unlocks composition. Since effects are values, they compose the same way pure functions do.

Say you have two operations: one reads a config file, another connects to a database. In imperative code, you'd run them sequentially and handle errors inline with try/catch. In an effect system, you compose them declaratively.

In F# with FIO:

```fsharp
let initialize = fio {
    let! config = readConfigFile "app.json"
    let! db = connectToDatabase config.ConnectionString
    return (config, db)
}
```

And the same in Scala with ZIO:

```scala
val initialize = for {
  config <- readConfigFile("app.json")
  db     <- connectToDatabase(config.connectionString)
} yield (config, db)
```

`initialize` is still just a value. It describes a program that reads a config, then connects to a database, then returns both. If either step fails, the error propagates through the effect type, not through thrown exceptions.

This composability extends to parallelism. Want both operations to run concurrently instead of sequentially?

In F# with FIO:

```fsharp
let both = fetchUsers.ZipPar fetchOrders
```

And in Scala with ZIO:

```scala
val both = fetchUsers.zipPar(fetchOrders)
```

You state the intent; the runtime deals with threads and synchronization.

## The Runtime

An effect description is inert. It won't do anything until a runtime interprets it. The runtime is the boundary between your pure, composable program and the messy real world.

This separation has a practical consequence that's easy to overlook: you can change how effects are fulfilled without changing the program itself. In ZIO, you provide different `ZLayer` implementations: production layers that hit real databases, test layers backed by in-memory fakes. A program's type, `ZIO[R, E, A]`, declares what it needs, and you provide those dependencies when you run it. This is what makes ZIO programs testable without mocking frameworks or runtime DI containers.

FIO's type, `FIO<'A, 'E>`, has no environment parameter. What you can swap there is the runtime itself, from the single-threaded `DirectRuntime` (handy for tests) to the work-stealing `WorkStealingRuntime` that is the default. That changes how effects are scheduled, not what they do: the program description stays the same, and only the execution strategy changes.

## Concurrency Without the Pain

Traditional concurrent programming means managing threads, locks, shared state, and error handling across all of it. Even with async/await, you're still tracking `Task` lifecycles, cancellation tokens, and exceptions that surface in surprising places. Building FIO's runtimes gave me a healthy respect for this: some of the hardest debugging I've ever done was in FIO's own scheduler, where bugs rarely reproduce when you're actually looking for them.

Effect systems introduce *fibers*: lightweight green threads managed by the runtime, not the OS. Creating a fiber is cheap (you can have thousands or millions), and the runtime handles scheduling across available threads. In F# with FIO:

```fsharp
let processBatch = FIO.forEachPar items processItem
```

And the same in Scala with ZIO:

```scala
val processBatch = ZIO.foreachPar(items)(processItem)
```

Each item gets its own fiber. The runtime schedules them efficiently. If one fails, the others can be interrupted and cleaned up automatically.

This is *structured concurrency*: fibers are scoped to their parent. When a parent effect completes or fails, its child fibers are cancelled and their resources released. Nothing outlives its context.

Writing the same batch by hand — spawning tasks, aggregating results, handling cancellation and partial failures — is several times the code, and the hard parts are exactly the ones you'll get subtly wrong.

## The Ecosystem

Effect systems aren't new, but they've matured a lot in the last few years. A caveat before the tour: I haven't used ZIO or Cats Effect in production. I know them from the months I spent studying them for my thesis, so read this as a researcher's impression rather than a practitioner's.

**ZIO** is the most prominent in the Scala world. It's batteries-included: dependency injection, typed error handling, streaming, scheduling, and a large standard library. ZIO programs use `ZIO[R, E, A]` where `R` is the environment (dependencies), `E` is the error type, and `A` is the success type. All three are visible in the type signature, so you can see at a glance what a program needs, how it can fail, and what it produces.

**Cats Effect** takes a different path in the same ecosystem. It ships a concrete `IO` type, but what sets it apart is its typeclasses: libraries are written against them, so they work with any effect type that implements them. Where ZIO is a framework, Cats Effect is more of a standard that libraries build on. It powers http4s, fs2, and doobie, giving you a composable toolkit rather than a single opinionated stack.

**FIO** is my own contribution. I wrote my thesis at DTU in 2022, supervised by [Alceste Scalas](https://people.compute.dtu.dk/~alcsc/), who offered two directions: write a compiler targeting ZIO in Scala, or bring ZIO's ideas to F#, where nothing like it existed. I chose the second. The hardest part wasn't the DSL — it was the runtime. ZIO leans on the JVM's type erasure, while .NET generics are reified, so FIO has to emulate type erasure at runtime to interpret effects of arbitrary types. FIO today has fibers, typed channels, and four interchangeable runtimes, from the single-threaded `DirectRuntime` to the work-stealing default. It's available on [NuGet](https://www.nuget.org/packages/FSharp.FIO), and the [project page](/projects/fio) has the details.

What all three share is the same core insight: treat effects as values, compose them purely, and let a runtime handle execution.

## Why This Matters

If the principles of functional programming are about how to *think* about code, effect systems are about building real programs without abandoning those principles. Separation of effects becomes a guarantee, enforced by types and managed by a runtime, instead of a convention that survives only as long as the team's discipline.

I should be honest about where I stand, though: I don't use effect systems at work. My day job is Elixir, where concurrency means processes and supervision trees rather than typed effects — the BEAM answers many of the same questions differently: isolate the failure in a process, let it crash, have a supervisor restart it. So FIO remains a nights-and-weekends project. What transfers is the mindset: I'm far more deliberate about where side effects live, and in Elixir that's discipline, not something the compiler enforces.
