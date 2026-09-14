using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace Architecture.Tests;

public class HostArchitectureTests
{
    [Fact]
    public void Modules_Should_Not_Depend_On_Hosts()
    {
        // Assemblies / namespaces that represent host applications.
        string[] hostNamespaces =
        {
            "DES.Starter.Api"
        };

        var result = Types
            .InCurrentDomain()
            .That()
            .ResideInNamespace("DES.Modules")
            .Should()
            .NotHaveDependencyOnAny(hostNamespaces)
            .GetResult();

        var failingTypes = result.FailingTypeNames ?? Array.Empty<string>();

        result.IsSuccessful.ShouldBeTrue(
            "Module code must not depend on host assemblies. " +
            $"Failing types: {string.Join(", ", failingTypes)}");
    }

    [Fact]
    public void Hosts_Should_Not_Depend_On_Module_Internals()
    {
        // Hosts may depend on module contracts and module root types,
        // but should not directly reference feature or data-layer namespaces.
        string[] forbiddenNamespaces =
        {
            "DES.Modules.Auditing.Features",
            "DES.Modules.Auditing.Data",
            "DES.Modules.Chat.Features",
            "DES.Modules.Chat.Data",
            "DES.Modules.Chat.Domain",
            "DES.Modules.Identity.Features",
            "DES.Modules.Identity.Data",
            "DES.Modules.Multitenancy.Features",
            "DES.Modules.Multitenancy.Data"
        };

        var hostResult = Types
            .InCurrentDomain()
            .That()
            .ResideInNamespace("DES.Starter")
            .Should()
            .NotHaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        var hostFailingTypes = hostResult.FailingTypeNames ?? Array.Empty<string>();

        hostResult.IsSuccessful.ShouldBeTrue(
            "Hosts should not depend directly on module feature or data internals. " +
            $"Failing types: {string.Join(", ", hostFailingTypes)}");
    }
}

internal static class ModuleArchitectureTestsFixture
{
    public static readonly string SolutionRoot = GetSolutionRoot();

    private static string GetSolutionRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Unable to locate solution root containing 'src' folder.");
        }

        return directory.FullName;
    }
}
