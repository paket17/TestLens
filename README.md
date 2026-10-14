# TestLens 🧪🔍

Инструмент статического анализа на базе **.NET Roslyn** для поиска и запуска автоматизированных тестов, покрывающих конкретный метод C#-кода — с полной поддержкой косвенных вызовов любой глубины, интерфейсного диспетчеризирования, полиморфных переопределений и различных тестовых фреймворков.

---

## 📌 Возможности

- **Поиск тестов по цепочке вызовов (Inverted Call Graph BFS):**
  - Обнаруживает не только прямые вызовы, но и косвенные через промежуточные сервисы/хэндлеры любой глубины (1, 2, 3+ уровней).
  - Распознает вызовы интерфейсов (`IFoo.Bar()` -> `Foo.Bar()`).
  - Учитывает виртуальные переопределения базовых классов (`override`).
  - Разматывает лямбды, делегаты и локальные функции к их содержащим методам.
  - Защита от зацикливаний и взаимной рекурсии в графе вызовов.
- **Поддержка тестовых фреймворков:**
  - **xUnit** (`[Fact]`, `[Theory]`)
  - **NUnit** (`[Test]`, `[TestCase]`, `[TestCaseSource]`)
  - **MSTest** (`[TestMethod]`, `[DataTestMethod]`)
  - Поддержка `DisplayName` атрибутов и легкая расширяемость через паттерн детекторов.
- **Visual Studio 2022 / 2026 Extension (VSIX):**
  - Пункт в контекстном меню редактора C# кода: `TestLens: Find Covering Tests...`.
  - Отдельное окно инструментов (**Tool Window**):
    - Статусный бейдж: **COVERED** (зеленый), **NOT COVERED** (красный), нейтральный **SEARCHING...** во время анализа.
    - **Live-поток обнаружения тестов:** найденные тесты отображаются в списке моментально по мере обхода графа вызовов, не дожидаясь завершения всего анализа.
    - **Остановка поиска:** во время сканирования кнопка **Refresh** превращается в кнопку **Stop** — можно остановить поиск в любой момент с полным сохранением уже найденных результатов.
    - Дерево иерархии вызовов со сниппетами строк кода и номерами строк.
    - Удобная навигация: двойной клик по шагу или тесту мгновенно открывает файл на нужной строке в редакторе.
    - Контекстное меню (ПКМ): быстрое копирование имени теста, полного имени (FQN), пути к файлу и шага вызова.
    - Горизонтальная прокрутка дерева через `Shift + MouseWheel`.
    - Кнопки **▶ Запуск** для каждого отдельного теста и **▶ Запустить все** с выводом прогресса в Visual Studio Output Window.
- **CLI Утилита (TestLens.Cli):**
  - Автономный запуск в терминале или CI/CD.
  - Цветной вывод дерева вызовов.
  - Флаг `--json` для программной интеграции (например, расширения для **VS Code**).
  - Флаг `--run` для немедленного выполнения найденных тестов через `dotnet test`.
- **Архитектура (SOLID / DRY / Clean Architecture):**
  - Ядро `TestLens.Core` независимо от среды IDE (`netstandard2.0`, `net8.0`, `net10.0`).
  - Легковесный встроенный IoC/DI контейнер (`CoreServiceCollection`, `CoreServiceProvider`), совместимый с `Microsoft.Extensions.DependencyInjection`.

---

## 🏛️ Структура проекта

```
TestLens/
├── src/
│   ├── TestLens.Core/                  # Библиотека анализа и поиска тестов (netstandard2.0, net8.0, net10.0)
│   │   ├── Analysis/                   # Обходчик графа вызовов (RoslynInvertedCallGraphWalker, CallerResolver)
│   │   ├── Detection/                  # Детекторы тестов (xUnit, NUnit, MSTest, Composite)
│   │   ├── Execution/                  # Раннер тестов (DotNetCliTestRunner)
│   │   ├── IoC/                        # Легковесный DI-контейнер
│   │   ├── Models/                     # Модели данных (MethodCoverageResult, CallChain, CoveringTestMatch)
│   │   └── Extensions/                 # Методы расширения ServiceCollection
│   │
│   ├── TestLens.Cli/                   # Консольное приложение для поиска и запуска тестов
│   │   └── Program.cs                  # Загрузка MSBuildWorkspace, CLI интерфейс, вывод JSON
│   │
│   └── TestLens.Vsix/                  # Расширение для Visual Studio 2022 / 2026 (.NET Framework 4.8)
│       ├── Commands/                   # Команда контекстного меню (FindCoveringTestsCommand)
│       ├── ToolWindows/                # Окно TestLensToolWindow + WPF контрол
│       ├── ViewModels/                 # MVVM ViewModel дерева тестов и цепочек вызовов
│       └── VSCommandTable.vsct         # Регистрация меню в редакторе кода
│
└── tests/
    └── TestLens.Core.Tests/            # Модульные тесты ядра (15 тестов xUnit)
```

---

## 🚀 Использование CLI

### Примеры запуска на решении TestLens.sln

#### 1. Метод, покрытый тестами (с флагом `-r` для их немедленного запуска):
```bash
dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m CreateWorkspaceFromSource -r
```
> **Результат:** находит 9 покрывающих тестов xUnit из `TestLens.Core.Tests`, выводит дерево цепочек вызовов и запускает их через `dotnet test`:
> ```
> Target method: TestLens.Core.Tests.Helpers.TestWorkspaceHelper.CreateWorkspaceFromSource
> Coverage status: COVERED (found 9 test(s))
> ...
> ✓ ALL TESTS PASSED! (Passed: 9, Duration: 4.86s)
> ```

#### 2. Метод, не покрытый тестами:
```bash
dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m FindCoveringTestsAsync
```
> **Результат:**
> ```
> Target method: TestLens.Core.Analysis.ICoveringTestFinder.FindCoveringTestsAsync
> Coverage status: NOT COVERED BY TESTS (0 calls from tests found)
> ```

#### 3. Вывод в формате JSON (для автоматизации и интеграций):
```bash
dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m CreateWorkspaceFromSource --json
```

> **💡 Зачем нужны два тире `--` перед аргументами в `dotnet run`?**  
> В CLI-команде `dotnet run` двойное тире `--` является стандартным разделителем (POSIX argument delimiter).  
> Оно говорит самой команде `dotnet run`: *"Здесь заканчиваются параметры dotnet run (например, `--project`, `--configuration`). Все последующие аргументы передай напрямую в программу `TestLens.Cli` в массив `args[]`"*.  
> Без этого разделителя `dotnet run` попытался бы интерпретировать флаги приложения (например, `-s` или `-r`) как свои собственные.  
> Если запускать скомпилированный бинарник напрямую (или через `dotnet TestLens.Cli.dll`), разделитель `--` указывать не нужно:  
> `TestLens.Cli.exe -s TestLens.sln -m CreateWorkspaceFromSource -r`

---

## 💻 Установка и использование в Visual Studio

### Установка расширения:
1. Соберите решение или возьмите готовый файл пакета:
   - `src/TestLens.Vsix/TestLens.vsix` или `src/TestLens.Vsix/bin/Debug/net48/TestLens.vsix`
2. Закройте Visual Studio и дважды кликните по `.vsix` файлу для установки.
3. Откройте проект в Visual Studio 2022 или Visual Studio 2026.

### Использование в коде:
1. Откройте любой C# файл и поставьте курсор внутри сигнатуры или тела метода.
2. Нажмите правой кнопкой мыши и выберите **"TestLens: Find Covering Tests..."** (или вызовите окно через меню `Вид -> Другие окна -> TestLens Window`).
3. В открывшейся панели отобразится:
   - Статус: покрыт или не покрыт (с нейтральным фоном во время поиска).
   - Список тестов с их фреймворками (xUnit, NUnit, MSTest), обновляемый в реальном времени.
   - Дерево вызовов от теста до целевого метода со сниппетами кода.
   - Кнопка **Stop** для возможности остановить анализ в любой момент.
   - Кнопки для перехода к строкам кода и запуска тестов.

---

## 🧪 Запуск модульных тестов

```bash
dotnet test tests/TestLens.Core.Tests/TestLens.Core.Tests.csproj
```
