# Migrating SharpTS GUI API 1 to API 2

GUI API 2 begins with `SharpTS.Gui.Sdk` `0.2.0-preview.1`. Preview APIs may change between
`0.2.0-preview.*` releases. Hosted ABI remains version 1; guest scheduling and host initialization
are unchanged.

## Upgrade

Update the SDK version in the project declaration:

```xml
<Project Sdk="SharpTS.Gui.Sdk/0.2.0-preview.1">
```

Clean and rebuild the project so the SDK regenerates `.sharpts/app.json` with `guiApiVersion: 2`.
The API 2 host intentionally rejects an API 1 manifest rather than attempting an unsafe roll
forward.

## Renderer changes in preview.1

- A keyed function component now owns its complete logical subtree. Moving it preserves its hook
  state and every compatible native control below it.
- A keyed fragment owns all of its children but is layout-transparent. API 1 inserted an implicit
  vertical `StackPanel`; layouts that relied on that panel must add an explicit `StackPanel`.
- Duplicate intrinsic, component, or fragment keys at the same sibling level fail before the
  native tree is changed.
- `useEffect` setup and cleanup run after render mode ends. Synchronous state updates from an
  effect queue a later render instead of producing a render-phase update error.
- Keyboard handlers are diffed on update. Adding or removing `onKeyDown`/`onKeyUp` no longer
  requires remounting, and `repeat` reflects held-key state for the top-level window.
- `ErrorBoundary` can recover descendant render and effect setup/cleanup failures with a typed
  `fallback(error, reset)` callback.
- Failed native property commits are reversed to the last committed tree. If recovery itself
  fails, the damaged window root is disposed and the host receives a fatal combined error.
- JSX now checks generic function-component inference, callable-object signatures, declared
  `children`/`ref` prop types, and the `key` type from `JSX.IntrinsicAttributes`.

`renderDesktop` remains available in preview.1. It will be replaced by the multi-window
`createDesktopApplication` API in preview.2; applications may defer that source migration until
that preview is adopted.
