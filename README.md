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
    - Статусный бейдж: **ПОКРЫТ ТЕСТАМИ** (зеленый) / **НЕ ПОКРЫТ ТЕСТАМИ** (красный).
    - Дерево иерархии вызовов со сниппетами строк кода и номерами строк.
    - Двойной клик по шагу или тесту — мгновенный переход к файлу и строке в редакторе.
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

## 🏛 Структура проекта

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

```bash
# Базовый поиск тестов по методу:
dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m FindCoveringTestsAsync

# Поиск и немедленный запуск найденных тестов:
dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m ProcessOrder -r

# Вывод в формате JSON (для интеграций или VS Code):
dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m ProcessOrder --json
```

---

## 💻 Установка и использование в Visual Studio

### Установка готового расширения:
1. Соберите решение или возьмите готовый файл пакета:
   `src/TestLens.Vsix/bin/Debug/net48/TestLens.vsix`
2. Закройте Visual Studio и дважды кликните по `.vsix` файлу для установки.
3. Откройте проект в Visual Studio 2022 или Visual Studio 2026.

### Использование в коде:
1. Откройте любой C# файл и поставьте курсор внутри сигнатуры или тела метода.
2. Нажмите правой кнопкой мыши и выберите **"TestLens: Find Covering Tests..."** (или вызовите окно через меню `Вид -> Другие окна -> TestLens Window`).
3. В открывшейся панели отобразится:
   - Статус: покрыт или не покрыт.
   - Список тестов с их фреймворками (xUnit, NUnit, MSTest).
   - Дерево вызовов от теста до целевого метода.
   - Кнопки для перехода к строкам кода и запуска тестов.

---

## 🧪 Запуск модульных тестов

```bash
dotnet test tests/TestLens.Core.Tests/TestLens.Core.Tests.csproj
```
