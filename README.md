# TestLens 🧪🔍

[🇷🇺 Читать на русском](README.ru.md)

A static analysis tool powered by **.NET Roslyn** for discovering and running automated tests that cover a specific C# method — featuring full support for indirect call chains of arbitrary depth, interface dispatch, polymorphic overrides, and various test frameworks.

<img width="1576" height="411" alt="testlens" src="https://github.com/user-attachments/assets/f2d15a0a-3eb6-4c49-963a-4313a786c59d" />

---

## 📌 Features

- **Test Discovery via Inverted Call Graph BFS:**
  - Detects not only direct invocations, but also indirect call chains through intermediate services and handlers of any depth (1, 2, 3+ levels).
  - Resolves interface dispatch (`IFoo.Bar()` -> `Foo.Bar()`).
  - Accounts for virtual method overrides in derived classes (`override`).
  - Unwinds lambdas, delegates, and local functions back to their enclosing methods.
  - Cycle detection and recursion protection in call graphs.
- **Supported Test Frameworks:**
  - **xUnit** (`[Fact]`, `[Theory]`)
  - **NUnit** (`[Test]`, `[TestCase]`, `[TestCaseSource]`)
  - **MSTest** (`[TestMethod]`, `[DataTestMethod]`)
  - Supports `DisplayName` attributes and extensibility via a detector pattern.
- **Visual Studio 2022 / 2026 Extension (VSIX):**
  - Context menu item in the C# code editor: `TestLens: Find Covering Tests...`.
  - Dedicated **Tool Window**:
    - Status badge: **COVERED** (green), **NOT COVERED** (red), neutral during search.
    - **🔄 Refresh** button: re-run analysis for the current method (e.g. after modifying code). During analysis, the button transforms into **⏹ Stop**, preserving matches found so far.
    - **▶ Run** buttons for each individual test and **▶ Run All** with live progress logged to the Visual Studio Output Window (during test execution, the button switches to **⏹ Stop**).
    - Call hierarchy tree with code snippets and line numbers.
- **CLI Utility (TestLens.Cli):**
  - Standalone execution in terminal or CI/CD pipelines.
  - Colored call tree output.
  - `--json` flag for programmatic integration (e.g., extensions for **VS Code**).
  - `--run` flag for immediate execution of discovered tests via `dotnet test`.
- **Architecture (Pure DI / Clean Architecture):**
  - Core library `TestLens.Core` is IDE-independent (`netstandard2.0`, `net10.0`).
  - Fast, transparent **Pure DI** (`TestLensServices`): static factories allowing mock injection and custom implementations without heavy IoC containers or reflection overhead.

---

## 🏛️ Project Structure

```
TestLens/
├── src/
│   ├── TestLens.Core/                  # Core test discovery and analysis library (netstandard2.0, net10.0)
│   │   ├── Analysis/                   # Call graph traversal (RoslynInvertedCallGraphWalker, CallerResolver)
│   │   ├── Compatibility/              # netstandard2.0 compatibility polyfills (WaitForExitAsync)
│   │   ├── Detection/                  # Test detectors (xUnit, NUnit, MSTest, Composite)
│   │   ├── Execution/                  # Test execution runner (DotNetCliTestRunner)
│   │   ├── Models/                     # Data models (MethodCoverageResult, CallChain, CoveringTestMatch)
│   │   └── TestLensServices.cs         # Pure DI service factory
│   │
│   ├── TestLens.Cli/                   # CLI console application for finding and running tests (net10.0)
│   │   └── Program.cs                  # MSBuildWorkspace loading, CLI interface, JSON serialization
│   │
│   └── TestLens.Vsix/                  # Extension for Visual Studio 2022 / 2026 (.NET Framework 4.8)
│       ├── Commands/                   # Context menu command (FindCoveringTestsCommand)
│       ├── ToolWindows/                # Tool window TestLensToolWindow + WPF control
│       ├── ViewModels/                 # MVVM ViewModel for test tree and call chains
│       └── VSCommandTable.vsct         # Code editor menu registration
│
└── tests/
    └── TestLens.Core.Tests/            # Core unit tests (15 xUnit tests)
```

---

## 🚀 CLI Usage

### Examples on TestLens.sln

#### 1. Method covered by tests (using `-r` flag to run them immediately):
```bash
dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m CreateWorkspaceFromSource -r
```
> **Result:** Finds 9 covering xUnit tests from `TestLens.Core.Tests`, displays the call chain tree, and executes them via `dotnet test`:
> ```
> Target method: TestLens.Core.Tests.Helpers.TestWorkspaceHelper.CreateWorkspaceFromSource
> Coverage status: COVERED (found 9 test(s))
> ...
> ✓ ALL TESTS PASSED! (Passed: 9, Duration: 4.86s)
> ```

#### 2. Method not covered by tests:
```bash
dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m PrintBanner
```
> **Result:**
> ```
> Target method: TestLens.Cli.Program.PrintBanner
> File: D:\Repos\TestLens\src\TestLens.Cli\Program.cs:158
> Coverage status: NOT COVERED BY TESTS (0 calls from tests found)
> ```

#### 3. Output in JSON format (for automation and integrations):
```bash
dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m CreateWorkspaceFromSource --json
```

> **💡 Why are double dashes `--` needed before arguments in `dotnet run`?**  
> In the `dotnet run` CLI command, the double dash `--` is the standard POSIX argument delimiter.  
> It tells `dotnet run`: *"Options for `dotnet run` itself (e.g. `--project`, `--configuration`) end here. Pass all subsequent arguments directly to `TestLens.Cli` in `args[]`"*.  
> Without this delimiter, `dotnet run` would attempt to interpret application flags (such as `-s` or `-r`) as its own parameters.  
> When executing the compiled binary directly (or via `dotnet TestLens.Cli.dll`), the `--` delimiter is not required:  
> `TestLens.Cli.exe -s TestLens.sln -m CreateWorkspaceFromSource -r`

---

## 💻 Building, Installing, and Using the Visual Studio Extension

### 📦 Prerequisites for Building
To build the VSIX project, Visual Studio components (2022 or 2026) are required:
- Workload: **Visual Studio extension development**.
- Targeting / Developer Pack: **.NET Framework 4.8 Targeting Pack / Developer Pack**.

---

### 🔨 How to Build the VSIX Extension

The extension version is specified in `source.extension.vsixmanifest`.

The `.vsix` file is automatically packaged during a standard project build.

---

### 📥 Installing the Built Extension
1. Be sure to **close all open Visual Studio windows**.
2. In File Explorer, double-click the generated `src/TestLens.Vsix/TestLens.vsix` file.
3. In the **VSIX Installer** window, confirm installation to the desired Visual Studio versions (2022/2026) by clicking **Install** (or **Modify**).
4. Launch Visual Studio.

---

### 🎯 Using the Extension in the Code Editor:
1. Open any C# file and place the caret on a method signature or inside its body.
2. Right-click and choose **"TestLens: Find Covering Tests..."** (the tool window can also be opened via `View ➔ Other Windows ➔ TestLens Window`).  
   *(If the caret is outside a method, no blocking modal popups appear — the tool window status bar simply displays a hint: `Place the caret inside a method declaration...`).*
3. The opened panel displays:
   - Coverage status: **COVERED** (green) or **NOT COVERED** (red).
   - Test list (xUnit, NUnit, MSTest) updating in real time during analysis.
   - Call hierarchy tree leading from the test method down to the target method with line snippets.
   - **🔄 Refresh** button: re-run test discovery for the current method (switches to **⏹ Stop** during search to cancel).
   - Test execution buttons (**▶ Run** for an individual test / **▶ Run All**), which also toggle to **⏹ Stop** during execution to allow cancellation.

---

## 🧪 Running Unit Tests

```bash
dotnet test TestLens.sln
```
