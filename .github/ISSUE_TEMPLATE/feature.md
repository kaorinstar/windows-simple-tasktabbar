---
name: Feature
about: A new feature or a change in how the application behaves, scoped before work starts
title: ''
labels: enhancement
assignees: ''
---

<!--
Fill in sections 1-8 before the issue is approved. They settle the scope and what has to be decided
first; the design itself belongs in the pull request. A fix, the build or the documentation uses
the Task template instead.
-->

## 1. Who and for what

<!-- The situation the user is in and the switch they are trying to make, in one sentence. -->

## 2. What it reads

<!-- The windows, events, settings or system state it depends on. -->

## 3. What it changes

<!-- What the user sees on the bar, the tray and the settings, and what is written to disk or the
registry. Name every new string: it goes into all twelve tables in UiStrings.cs. -->

## 4. Settings and what each one affects

<!-- One row per setting. A setting with nothing to affect is not added. Write "None" if there
are none. -->

| Setting | What it affects |
| --- | --- |
|  |  |

## 5. No-gos

<!-- What this issue does not do. -->

- 

### Fixed premises

<!-- What stays as it is. Delete the ones this issue cannot touch anyway, and add any others. -->

- The net48 build runs on the .NET Framework 4.8 that ships with Windows.
- The application is a single executable.
- The three-step activation in `Services/WindowService.cs` is kept.
- The bar displays windows and switches between them. It never merges them.

### Open to change

<!-- Existing behaviour this issue may replace. What is listed here is not argued back later. -->

- 

## 6. Rabbit holes

<!-- The unknowns to settle before writing code, and how each will be settled. -->

- 

## 7. Appetite

<!--
The upper limit of time this is worth, set before starting rather than estimated. For example: one
session. When the work runs past it, the scope is cut and the time is not extended. What was cut is
left as a comment on this issue.
-->

## 8. Done when

### Checked by the tests

- [ ] 

### Checked by hand on Windows

- [ ] 
