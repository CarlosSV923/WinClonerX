using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NetArchTest.Rules;
using Shouldly;
using WinCopy.Application.Ports;
using WinCopy.Domain.Shared;
using Xunit;

namespace WinCopy.ArchitectureTests;

/// <summary>Reglas de dependencias entre capas (arquitectura hexagonal) y exclusión de HTTP en el cliente (FR-027).</summary>
public sealed class LayeringTests
{
    private const string InfrastructureAssemblyName = "WinCopy.Infrastructure.Windows";

    private static Assembly DomainAssembly => typeof(ByteSize).Assembly;

    private static Assembly ApplicationAssembly => typeof(IClock).Assembly;

    // Los tipos de infraestructura son internos, así que el ensamblado se carga por nombre.
    private static Assembly InfrastructureAssembly => Assembly.Load(InfrastructureAssemblyName);

    private static Assembly AppAssembly => typeof(WinCopy.App.App).Assembly;

    [Fact]
    public void DomainDoesNotDependOnPlatformOrFrameworkNamespaces()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "System.Management",
                "System.Windows",
                "Microsoft.Win32",
                "WinCopy.Application",
                "WinCopy.Infrastructure",
                "WinCopy.App",
                "Serilog",
                "Microsoft.Extensions")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    [Fact]
    public void DomainDoesNotUseMarshalOrSafeHandle()
    {
        // NetArchTest compara espacios de nombres, no tipos concretos, así que estos tipos se buscan
        // en la tabla de referencias de tipos del ensamblado. Los atributos de interop quedan permitidos.
        var forbidden = FindTypeReferences(DomainAssembly, IsMarshalOrSafeHandleType);

        forbidden.ShouldBeEmpty("Domain types referencing Marshal or SafeHandle: " + string.Join(", ", forbidden));
    }

    [Fact]
    public void ApplicationDoesNotDependOnInfrastructureOrPlatformNamespaces()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "WinCopy.Infrastructure",
                "WinCopy.App",
                "System.Windows",
                "System.Management",
                "Serilog")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailureMessage(result));
    }

    [Fact]
    public void NoLayerDependsOnSystemNetHttp()
    {
        var failures = new List<string>();

        foreach (var (layer, assembly) in Layers())
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("System.Net.Http")
                .GetResult();

            if (!result.IsSuccessful)
            {
                failures.Add($"{layer} (types): {FailingNames(result)}");
            }

            if (ReferencesAssembly(assembly, "System.Net.Http"))
            {
                failures.Add($"{layer} (assembly reference): System.Net.Http");
            }
        }

        (failures.Count == 0).ShouldBeTrue("Layers depending on System.Net.Http: " + string.Join("; ", failures));
    }

    [Fact]
    public void DependencyRulesDetectKnownDependencies()
    {
        // Controles: el adaptador de Windows sí usa interop. Si estas comprobaciones no lo detectan,
        // las reglas de arriba serían vacías y no comprobarían nada.
        var namespaceControl = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn("System.Runtime.InteropServices")
            .GetResult();

        namespaceControl.IsSuccessful.ShouldBeFalse("Expected the Windows adapter to depend on System.Runtime.InteropServices.");

        var typeControl = FindTypeReferences(InfrastructureAssembly, IsMarshalOrSafeHandleType);

        typeControl.ShouldNotBeEmpty("Expected the Windows adapter to reference Marshal or SafeHandle types.");
    }

    private static (string Layer, Assembly Assembly)[] Layers() =>
    [
        ("Domain", DomainAssembly),
        ("Application", ApplicationAssembly),
        ("Infrastructure.Windows", InfrastructureAssembly),
        ("App", AppAssembly),
    ];

    private static bool IsMarshalOrSafeHandleType(string ns, string name) =>
        (ns == "System.Runtime.InteropServices" && name is "Marshal" or "SafeHandle")
        || ns == "Microsoft.Win32.SafeHandles";

    private static List<string> FindTypeReferences(Assembly assembly, Func<string, string, bool> predicate)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var matches = new List<string>();

        foreach (var handle in metadata.TypeReferences)
        {
            var reference = metadata.GetTypeReference(handle);
            var ns = metadata.GetString(reference.Namespace);
            var name = metadata.GetString(reference.Name);

            if (predicate(ns, name))
            {
                matches.Add($"{ns}.{name}");
            }
        }

        return matches;
    }

    private static bool ReferencesAssembly(Assembly assembly, string name) =>
        assembly.GetReferencedAssemblies()
            .Any(reference => string.Equals(reference.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string FailingNames(NetArchTest.Rules.TestResult result) =>
        result.FailingTypeNames is null ? "(none)" : string.Join(", ", result.FailingTypeNames);

    private static string FailureMessage(NetArchTest.Rules.TestResult result) =>
        "Types with a forbidden dependency: " + FailingNames(result);
}
