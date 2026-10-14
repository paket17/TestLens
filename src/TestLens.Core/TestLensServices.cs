using TestLens.Core.Extensions;
using TestLens.Core.IoC;

namespace TestLens.Core;

/// <summary>
/// Фабрика для быстрого создания и разрешения сервисов TestLens.
/// </summary>
public static class TestLensServices
{
    /// <summary>
    /// Создает сконфигурированный экземпляр <see cref="IServiceProvider"/>.
    /// </summary>
    public static IServiceProvider CreateServiceProvider()
    {
        var services = new CoreServiceCollection();
        services.AddTestLensCore();
        return services.BuildServiceProvider();
    }
}
