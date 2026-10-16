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
  - Поддержка атрибутов `DisplayName` и расширяемость через паттерн детекторов.
- **Visual Studio 2022 / 2026 Extension (VSIX):**
  - Пункт в контекстном меню редактора C# кода: `TestLens: Find Covering Tests...`.
  - Отдельное окно инструментов (**Tool Window**):
    - Статусный бейдж: **COVERED** (зеленый), **NOT COVERED** (красный), нейтральный во время поиска.
    - Кнопка **🔄 Refresh (Обновить)**: повторный запуск анализа текущего метода (например, после изменения кода). Во время поиска кнопка превращается в **⏹ Stop** с сохранением уже найденных результатов.
    - Кнопки **▶ Запуск** для каждого отдельного теста и **▶ Запустить все** с выводом прогресса в Visual Studio Output Window (во время запуска теста кнопка превращается в **⏹ Stop**).
    - Дерево иерархии вызовов со сниппетами строк кода и номерами строк.
- **CLI Утилита (TestLens.Cli):**
  - Автономный запуск в терминале или CI/CD.
  - Цветной вывод дерева вызовов.
  - Флаг `--json` для программной интеграции (например, расширения для **VS Code**).
  - Флаг `--run` для немедленного выполнения найденных тестов через `dotnet test`.
- **Архитектура (Pure DI / Clean Architecture):**
  - Ядро `TestLens.Core` независимо от среды IDE (`netstandard2.0`, `net10.0`).
  - Быстрый и прозрачный **Pure DI** (`TestLensServices`): статические фабрики с возможностью внедрения моков/кастомных реализаций без тяжелых IoC-контейнеров и рефлексии.

---

## 🏛️ Структура проекта

```
TestLens/
├── src/
│   ├── TestLens.Core/                  # Библиотека анализа и поиска тестов (netstandard2.0, net10.0)
│   │   ├── Analysis/                   # Обходчик графа вызовов (RoslynInvertedCallGraphWalker, CallerResolver)
│   │   ├── Compatibility/              # Полифилы совместимости для netstandard2.0 (WaitForExitAsync)
│   │   ├── Detection/                  # Детекторы тестов (xUnit, NUnit, MSTest, Composite)
│   │   ├── Execution/                  # Раннер тестов (DotNetCliTestRunner)
│   │   ├── Models/                     # Модели данных (MethodCoverageResult, CallChain, CoveringTestMatch)
│   │   └── TestLensServices.cs         # Pure DI фабрика создания сервисов
│   │
│   ├── TestLens.Cli/                   # Консольное приложение для поиска и запуска тестов (net10.0)
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
dotnet run --project src/TestLens.Cli -- -s TestLens.sln -m PrintBanner
```
> **Результат:**
> ```
> Target method: TestLens.Cli.Program.PrintBanner
> File: D:\Repos\TestLens\src\TestLens.Cli\Program.cs:158
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

## 💻 Сборка, установка и использование расширения для Visual Studio

### 📦 Необходимые компоненты для сборки (Prerequisites)
Для сборки VSIX-проекта требуются компоненты Visual Studio (2022 или 2026):
- Рабочая нагрузка **«Разработка расширений Visual Studio»** (*Visual Studio extension development*).
- Пакет целевой платформы **.NET Framework 4.8 Targeting Pack / Developer Pack**.

---

### 🔨 Как собрать VSIX-расширение

Версия расширения указывается в файле `source.extension.vsixmanifest`

Файл `.vsix` упаковывается автоматически при обычной сборке проекта

---

### 📥 Установка собранного расширения
1. Обязательно **закройте все открытые окна Visual Studio**.
2. В Проводнике дважды кликните по сгенерированному файлу `src/TestLens.Vsix/TestLens.vsix`.
3. В окне **VSIX Installer** подтвердите установку в нужные версии Visual Studio (2022/2026), нажав кнопку **Install** (или **Modify**).
4. Запустите Visual Studio.

---

### 🎯 Использование расширения в редакторе кода:
1. Откройте любой C# файл и установите курсор на сигнатуру или внутри тела метода.
2. Нажмите правой кнопкой мыши и выберите **"TestLens: Find Covering Tests..."** (также панель можно открыть через меню `Вид ➔ Другие окна ➔ TestLens Window`).  
   *(Если курсор установлен вне метода, всплывающие окна не блокируют работу — в статусной строке панели отобразится подсказка: `Place the caret inside a method declaration...`).*
3. В открывшейся панели отобразится:
   - Статус покрытия: **COVERED** (зеленый) или **NOT COVERED** (красный).
   - Список тестов (xUnit, NUnit, MSTest), обновляемый в реальном времени во время анализа.
   - Дерево вызовов от тестового метода до целевого метода со сниппетами строк кода.
   - Кнопка **🔄 Refresh**: повторный запуск поиска тестов для текущего метода (во время поиска превращается в **⏹ Stop** для прерывания).
   - Кнопки запуска тестов (**▶ Запуск** для одного теста / **▶ Запустить все**), которые во время выполнения также превращаются в **⏹ Stop** для остановки.

---

## 🧪 Запуск модульных тестов

```bash
dotnet test TestLens.sln
```
