namespace TestLens.Core.Models;

/// <summary>
/// Поддерживаемые типы тестовых фреймворков.
/// </summary>
public enum TestFrameworkType
{
    Unknown = 0,
    XUnit = 1,
    NUnit = 2,
    MSTest = 3,
    Custom = 99
}
