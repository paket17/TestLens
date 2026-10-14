using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using System.Collections.Concurrent;
using TestLens.Core.Compatibility;

namespace TestLens.Core.IoC;

/// <summary>
/// Легковесная реализация <see cref="IServiceCollection"/>.
/// </summary>
public sealed class CoreServiceCollection : List<ServiceDescriptor>, IServiceCollection
{
    /// <summary>
    /// Создает экземпляр <see cref="IServiceProvider"/> на основе зарегистрированных дескрипторов.
    /// </summary>
    public IServiceProvider BuildServiceProvider() => 
        new CoreServiceProvider(this);
}

/// <summary>
/// Легковесный IoC контейнер, реализующий стандартный <see cref="IServiceProvider"/>.
/// Поддерживает Singleton, Transient, конструкторную инъекцию и разрешение коллекций (IEnumerable&lt;T&gt;).
/// </summary>
public sealed class CoreServiceProvider : IServiceProvider
{
    private readonly IReadOnlyList<ServiceDescriptor> _descriptors;
    private readonly ConcurrentDictionary<ServiceDescriptor, object> _singletons = new();

    public CoreServiceProvider(IEnumerable<ServiceDescriptor> descriptors)
    {
        CompatExtensions.ThrowIfNull(descriptors, nameof(descriptors));
        _descriptors = [.. descriptors];
    }

    public object? GetService(Type serviceType)
    {
        CompatExtensions.ThrowIfNull(serviceType, nameof(serviceType));

        if (serviceType == typeof(IServiceProvider))
            return this;

        // Поддержка разрешения IEnumerable<T>
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var itemType = serviceType.GetGenericArguments()[0];
            var matches = _descriptors.Where(d => d.ServiceType == itemType).ToList();
            var listType = typeof(List<>).MakeGenericType(itemType);
            var list = (IList)Activator.CreateInstance(listType)!;

            foreach (var match in matches)
            {
                var instance = ResolveInstance(match);
                list.Add(instance);
            }

            return list;
        }

        var descriptor = _descriptors.LastOrDefault(d => d.ServiceType == serviceType);
        if (descriptor == null)
            return null;

        return ResolveInstance(descriptor);
    }

    private object ResolveInstance(ServiceDescriptor descriptor)
    {
        if (descriptor.Lifetime == ServiceLifetime.Singleton && _singletons.TryGetValue(descriptor, out var existing))
            return existing;

        object instance;

        if (descriptor.ImplementationInstance != null)
            instance = descriptor.ImplementationInstance;
        else if (descriptor.ImplementationFactory != null)
            instance = descriptor.ImplementationFactory(this);
        else if (descriptor.ImplementationType != null)
        {
            var ctor = descriptor.ImplementationType.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (ctor == null)
                throw new InvalidOperationException($"No public constructor found for type {descriptor.ImplementationType.FullName}");

            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                var arg = GetService(paramType);
                if (arg == null && !parameters[i].HasDefaultValue)
                    throw new InvalidOperationException($"Failed to resolve parameter '{parameters[i].Name}' type {paramType.FullName} for the service {descriptor.ImplementationType.FullName}");

                args[i] = arg ?? parameters[i].DefaultValue;
            }

            instance = ctor.Invoke(args);
        }
        else
            throw new InvalidOperationException($"Service descriptor {descriptor.ServiceType.FullName} does not contain an implementation type or a factory.");

        if (descriptor.Lifetime == ServiceLifetime.Singleton)
            _singletons.TryAdd(descriptor, instance);

        return instance;
    }
}
