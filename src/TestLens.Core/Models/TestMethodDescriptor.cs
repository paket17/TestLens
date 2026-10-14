namespace TestLens.Core.Models;

/// <summary>
/// Описание найденного тестового метода.
/// </summary>
public sealed record TestMethodDescriptor
{
    /// <summary>
    /// Полное квалифицированное имя метода (Namespace.ClassName.MethodName).
    /// </summary>
    public required string FullyQualifiedName { get; init; }

    /// <summary>
    /// Простое имя метода (MethodName).
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// Имя класса/типа, содержащего тест.
    /// </summary>
    public required string ClassName { get; init; }

    /// <summary>
    /// Имя пространства имен.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Имя проекта, в котором находится тест.
    /// </summary>
    public required string ProjectName { get; init; }

    /// <summary>
    /// Путь к файлу исходного кода.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Номер строки (1-based), где объявлен тест.
    /// </summary>
    public int LineNumber { get; init; }

    /// <summary>
    /// Определенный тестовый фреймворк.
    /// </summary>
    public TestFrameworkType Framework { get; init; }

    /// <summary>
    /// Отображаемое имя (например, из атрибута DisplayName или сигнатуры).
    /// </summary>
    public string? DisplayName { get; init; }

    public override string ToString() => 
        FullyQualifiedName;
}
