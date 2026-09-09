namespace Integration.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class DesCollectionDefinition : ICollectionFixture<DesWebApplicationFactory>
{
    public const string Name = "DesIntegration";
}
