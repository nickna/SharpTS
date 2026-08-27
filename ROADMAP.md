# SharpTS roadmap

SharpTS is building a high-performance TypeScript runtime and application platform powered by
.NET. Our goal is to make TypeScript a strong choice for web backends, native desktop and command-
line applications, mobile applications, and software that integrates with the .NET and native
ecosystems.

This is a living vision document, not a promise that every detail will arrive exactly as first
described. Priorities and target releases may change as we learn from implementation and users.
When they do, this document will change with them.

## How to read this roadmap

Milestones describe product outcomes rather than every issue required to reach them. Features can
ship as soon as they are ready, but a milestone is complete only when all of its required outcomes
have reached their stated level of support. Items explicitly described as experimental or
best-effort do not block a milestone.

Version numbers are current targets, not deadlines. Compatibility claims refer to tested,
documented behavior rather than complete Node.js, JavaScript, or TypeScript conformance. SharpTS
does not need to implement every API to be useful, but the applications and workflows we claim to
support must work reliably.

## Milestone 0 — Core product readiness (target: v1.5)

### Compiled performance

Compiled SharpTS will be at least 5% faster than the pinned Node.js LTS baseline in at least 75% of
a frozen, versioned cross-runtime benchmark suite on both Windows and Linux.

The benchmark specification will define representative inputs before results are evaluated. Each
benchmark family will receive equal weight so that adding parameter variations cannot change the
result disproportionately. Small inputs will remain visible in published results even when a
larger input is selected as the representative case. Cold start, compilation time, steady-state
throughput, memory use, and allocations will be reported separately rather than combined into one
score.

Initial Windows and Linux measurements may run on the same Windows 11 machine through native
Windows and WSL, preserving the same hardware for both operating systems. Automated benchmark
runs on dedicated or hosted runners are a future improvement, not a Milestone 0 requirement.
macOS performance is outside the current performance target.

### Node.js workload compatibility

SharpTS will run a published suite of representative Node.js command-line and web-backend
applications without changes to their application source. We will maintain a public compatibility
matrix covering tested package versions, built-in modules and APIs, package resolution, supported
workflows, and known deviations.

Milestone 0 focuses on practical foundations:

- a real command-line workload that exercises arguments, files, configuration, terminal output,
  errors, and exit behavior;
- a Web Standards-oriented server using Hono and its Node adapter;
- a conventional Express application covering routing, middleware, request bodies, static files,
  error handling, and streaming; and
- PostgreSQL access through `pg`, followed by typed queries and transactions through Drizzle ORM.

Fastify, production Next.js, and additional ORMs remain expansion targets described below. Passing
a trivial import or startup check is not enough: workload gates must exercise observable behavior,
lifecycle, error handling, and clean shutdown.

### Desktop applications

SharpTS will make TypeScript a production-ready choice for Windows x64 and Apple Silicon macOS
desktop applications. The application model will use TSX, functional components, hooks,
declarative rendering, reactive state, native controls, and headless testing. It should feel
familiar to web developers without claiming React compatibility.

Windows x64 and Apple Silicon macOS are the supported targets. Linux desktop support may be
offered on a best-effort experimental basis, but Linux certification and support do not block this
milestone. Intel macOS is outside the planned platform matrix.

### Command-line applications

SharpTS will build and distribute fast command-line applications for supported Windows, macOS,
and Linux targets. Milestone 0 artifacts may include a bundled managed .NET runtime, but users will
not need to install .NET separately.

Arguments, environment variables, standard streams, terminal behavior, signals, exit codes,
files, networking, child processes, and clean shutdown will be tested on each supported operating
system. Application-specific Native AOT output is a Milestone 1 outcome.

### .NET interoperability

SharpTS will provide a stable two-way .NET interoperability contract. TypeScript applications can
consume the .NET Base Class Library, external assemblies, and NuGet packages, while .NET
applications can consume compiled TypeScript assemblies.

The supported contract includes primitive and structured values, generics, tasks and asynchronous
operations, delegates, events, exceptions, and generated declarations. Interpreter and compiled
behavior should agree except where a deviation is explicitly documented. Native AOT supports the
same scenarios within its declared closed-world catalog and documents cases that require managed
execution.

## Milestone 1 — Tooling and native deployment (target: v2.0)

### Developer experience

The SharpTS VS Code extension will graduate to a production-supported release with project
diagnostics, navigation, .NET interoperability IntelliSense, reliable project configuration, and
debugging for interpreted and compiled applications. Editor behavior shared through the Language
Server Protocol will remain available to other editors where practical.

### Node.js application compatibility

The workload suite will expand beyond foundational servers and packages. Fastify will exercise
plugins, hooks, schema validation, asynchronous context, streams, and production server lifecycle.
Additional command-line workloads will cover process management, interactive terminal behavior,
and larger dependency graphs.

### Desktop integration and distribution

Desktop applications will gain system tray support, background lifecycle, multitouch and gesture
input, notifications, and deeper operating-system integration.

Distribution will use platform-appropriate artifacts: portable executables or MSIX packages on
Windows and signed, notarized application bundles on macOS. Applications will be self-contained
without requiring a preinstalled .NET runtime.

### Application-specific Native AOT

SharpTS will compile command-line applications into application-specific single native
executables, and supported desktop applications into Native AOT artifacts inside their appropriate
platform packages. Selected Windows, Linux, and Apple Silicon macOS runtime identifiers will be
supported. AOT output will be tested as a deployment mode with explicit startup, size, memory,
debugging, dynamic-code, and interoperability boundaries; it will not be presented as an
unconditional throughput improvement.

## Milestone 2 — Full-stack and mobile applications (target: v3.0)

### Full-stack Node.js workloads

SharpTS will run a production Next.js App Router application with server rendering, streaming,
route handlers, static assets, production build output, and a supported PostgreSQL ORM. Build-time
and production-runtime compatibility will be measured separately so that success in one phase
does not conceal a failure in the other.

The Node.js compatibility matrix will cover at least 50% of a versioned LTS API inventory at the
export/API level through executable tests. Application compatibility remains the primary product
outcome; the percentage is supporting evidence rather than the definition of usefulness.

Drizzle with `pg` is the first ORM target. Prisma is a subsequent target after the database driver,
generated-client, and build-tooling requirements can be tested as a coherent workflow.

### Language compatibility measurement

SharpTS will define and publish application-relevant JavaScript and TypeScript conformance goals.
These goals will improve confidence in supported programs without implying that complete Test262
or TypeScript compiler conformance is required for the product to succeed.

### Mobile applications

SharpTS will bring its declarative TSX application model to iOS and Android. Supported mobile
applications will include navigation, touch and gestures, accessibility, application lifecycle,
permissions, native services, testing, diagnostics, signing, packaging, and store deployment.

The milestone requires applications capable of satisfying Apple App Store and Google Play review
requirements, not only simulator demos or cross-compiled artifacts.

### C and C++ interoperability

SharpTS will generate TypeScript declarations and binding metadata from native headers. The first
portable contract will target C functions and C++ libraries that expose a stable C-compatible ABI,
including library loading, primitive types, strings, structures, pointers, callbacks, memory
ownership, threading, and error handling.

Direct C++ interoperability is also in scope, but C++ does not have one universal ABI. Support for
classes, overloaded functions, templates, exceptions, and compiler-mangled exports will therefore
be limited to explicitly supported compiler, standard-library, operating-system, and architecture
combinations or implemented through generated C-compatible wrappers.

## Milestone 3 — Wearable applications (target: v4.0)

SharpTS will target Wear OS and watchOS applications with platform-appropriate declarative UI,
small-screen navigation, touch and crown or bezel input, lifecycle, health and sensor permissions,
companion-device communication, packaging, store submission, and native-device testing.

Wearables are the first priority after core iOS and Android application support.

## Milestone 4 — Television applications (target: v5.0)

SharpTS will target Android TV and tvOS applications with focus and remote navigation, media and
playback integration, application lifecycle, packaging, store submission, and native-device
testing.

Television applications are the second platform-expansion priority after wearables.

## Milestone 5 — Mobile widgets (target: v6.0)

SharpTS will support Android App Widgets and iOS WidgetKit extensions with platform-appropriate
lifecycle, timelines and refresh policy, data sharing, deep links, packaging, and store review.
Android and iOS widgets have equal priority within this milestone.

Widgets are the third platform-expansion priority after wearables and television applications.

## Representative Node.js workload ladder

The compatibility suite should grow in deliberate stages rather than selecting packages only by
download count:

1. **Package and CLI foundations:** maintain the existing real-package smoke matrix, then add a
   pinned command-line application with filesystem, configuration, formatted output, and failure
   paths. Prettier is a strong candidate for the application-level gate; Commander remains useful
   as a focused package-level test.
2. **Web Standards baseline:** run Hono with `@hono/node-server`. Hono has a small dependency
   surface and helps distinguish Web API gaps from deeper Node.js compatibility gaps.
3. **Conventional Node backend:** run Express 5 with routing, middleware, JSON and form bodies,
   static files, cookies, errors, streaming, graceful shutdown, and concurrent requests.
4. **Structured production backend:** run Fastify with plugins, hooks, schema validation,
   asynchronous context, logging, streams, and lifecycle behavior.
5. **Database access:** test `pg` directly against PostgreSQL, then Drizzle over `pg`, including
   schema definition, CRUD, joins, parameterized queries, transactions, pooling, errors, and clean
   shutdown.
6. **Full-stack north star:** build and run a pinned Next.js production application using the App
   Router and a supported ORM. Cover server rendering, streaming, route handlers, caching, static
   assets, environment variables, and graceful shutdown.
7. **Second ORM:** add Prisma after its generated client, TypeScript query compiler, driver adapter,
   and required build steps can be validated end to end.

Every workload should pin its dependencies, run from a clean install, document allowed
configuration, avoid patches to application or dependency source, and assert externally observable
behavior. A compatibility claim should name the exact application and package versions that were
tested.
